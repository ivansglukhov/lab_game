using System.Threading;
using System.Threading.Tasks;
using Content.Server.Hands.Systems;
using Content.Server.Nii.Components;
using Content.Server.Nii.Systems;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Nii;

namespace Content.Server.Nii.HTN;

/// <summary>
/// Executes the first physical NII research workflow while HTN selects when it should run.
/// Per-researcher state lives in the work order and NPC steering component, not in this shared operator.
/// </summary>
public sealed partial class NiiExecuteResearchOrderOperator : HTNOperator
{
    private const float WorkRange = 1.1f;

    [Dependency] private IEntityManager _entityManager = default!;

    private HandsSystem _hands = default!;
    private NiiResearchMachineSystem _researchMachines = default!;
    private NiiResearchWorkOrderSystem _workOrders = default!;
    private NPCSteeringSystem _steering = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);

        _hands = sysManager.GetEntitySystem<HandsSystem>();
        _researchMachines = sysManager.GetEntitySystem<NiiResearchMachineSystem>();
        _workOrders = sysManager.GetEntitySystem<NiiResearchWorkOrderSystem>();
        _steering = sysManager.GetEntitySystem<NPCSteeringSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var valid = TryGetWorkOrder(blackboard, out _, out _, out var order) &&
                    order.Status != NiiWorkOrderStatus.Completed &&
                    order.Status != NiiWorkOrderStatus.Cancelled &&
                    order.Status != NiiWorkOrderStatus.Blocked;

        return Task.FromResult<(bool, Dictionary<string, object>?)>((valid, null));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!TryGetWorkOrder(blackboard, out var researcherUid, out var orderUid, out var order))
            return FinishMovement(blackboard, HTNOperatorStatus.Finished);

        if (order.Status == NiiWorkOrderStatus.Running)
        {
            _steering.Unregister(researcherUid);
            return HTNOperatorStatus.Continuing;
        }

        if (order.Status is NiiWorkOrderStatus.Completed or NiiWorkOrderStatus.Cancelled or NiiWorkOrderStatus.Blocked)
            return FinishMovement(blackboard, HTNOperatorStatus.Finished);

        if (order.Sample is not { } sampleUid ||
            _entityManager.Deleted(sampleUid) ||
            !_entityManager.HasComponent<NiiResearchSampleComponent>(sampleUid))
        {
            _workOrders.Block((orderUid, order), NiiWorkOrderBlockReason.NoSample);
            return FinishMovement(blackboard, HTNOperatorStatus.Finished);
        }

        if (order.Machine is not { } machineUid ||
            _entityManager.Deleted(machineUid) ||
            !_entityManager.TryGetComponent<NiiResearchMachineComponent>(machineUid, out var machine))
        {
            _workOrders.Block((orderUid, order), NiiWorkOrderBlockReason.MachineInaccessible);
            return FinishMovement(blackboard, HTNOperatorStatus.Finished);
        }

        if (!_hands.IsHolding(researcherUid, sampleUid))
        {
            if (!MoveIntoRange(researcherUid, sampleUid, out var movementStatus))
            {
                if (movementStatus == SteeringStatus.NoPath)
                    _workOrders.Block((orderUid, order), NiiWorkOrderBlockReason.SampleInaccessible);

                return movementStatus == SteeringStatus.NoPath
                    ? FinishMovement(blackboard, HTNOperatorStatus.Finished)
                    : HTNOperatorStatus.Continuing;
            }

            _steering.Unregister(researcherUid);
            if (!_hands.TryPickupAnyHand(researcherUid, sampleUid))
            {
                _workOrders.Block((orderUid, order), NiiWorkOrderBlockReason.SampleInaccessible);
                return HTNOperatorStatus.Finished;
            }

            if (!_workOrders.TryBeginDelivery((orderUid, order), sampleUid, machineUid))
                return HTNOperatorStatus.Failed;
        }

        if (!MoveIntoRange(researcherUid, machineUid, out var machineMovementStatus))
        {
            if (machineMovementStatus == SteeringStatus.NoPath)
                _workOrders.Block((orderUid, order), NiiWorkOrderBlockReason.MachineInaccessible);

            return machineMovementStatus == SteeringStatus.NoPath
                ? FinishMovement(blackboard, HTNOperatorStatus.Finished)
                : HTNOperatorStatus.Continuing;
        }

        _steering.Unregister(researcherUid);
        if (machine.IsProcessing)
        {
            _workOrders.Block((orderUid, order), NiiWorkOrderBlockReason.MachineBusy);
            return HTNOperatorStatus.Finished;
        }

        if (!_researchMachines.TryStartResearch((machineUid, machine), sampleUid, researcherUid))
        {
            if (order.Status != NiiWorkOrderStatus.Blocked)
                _workOrders.Block((orderUid, order), NiiWorkOrderBlockReason.MachineInaccessible);

            return HTNOperatorStatus.Finished;
        }

        _hands.TryDrop(researcherUid, sampleUid);
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        if (blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entityManager))
            _steering.Unregister(owner);
    }

    private bool TryGetWorkOrder(
        NPCBlackboard blackboard,
        out EntityUid researcherUid,
        out EntityUid orderUid,
        out NiiResearchWorkOrderComponent order)
    {
        researcherUid = default;
        orderUid = default;
        order = default!;

        if (!blackboard.TryGetValue(NPCBlackboard.Owner, out researcherUid, _entityManager) ||
            !_entityManager.TryGetComponent<NiiEmployeeComponent>(researcherUid, out var employee) ||
            employee.ActiveWorkOrder is not { } activeOrder ||
            !_entityManager.TryGetComponent<NiiResearchWorkOrderComponent>(activeOrder, out var workOrder) ||
            workOrder.AssignedTo != researcherUid)
            return false;

        orderUid = activeOrder;
        order = workOrder;
        return true;
    }

    private bool MoveIntoRange(EntityUid owner, EntityUid target, out SteeringStatus status)
    {
        var coordinates = _entityManager.GetComponent<TransformComponent>(target).Coordinates;
        var steering = _steering.Register(owner, coordinates);
        steering.Range = WorkRange;
        status = steering.Status;
        return status == SteeringStatus.InRange;
    }

    private HTNOperatorStatus FinishMovement(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        if (blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entityManager))
            _steering.Unregister(owner);

        return status;
    }
}
