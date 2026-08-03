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
/// Makes a laboratory technician physically collect reserved stock containers and deliver them to the reactor.
/// </summary>
public sealed partial class NiiExecuteProductionOrderOperator : HTNOperator
{
    private const float WorkRange = 1.1f;

    [Dependency] private IEntityManager _entityManager = default!;

    private HandsSystem _hands = default!;
    private NiiChemicalProductionSystem _production = default!;
    private NPCSteeringSystem _steering = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _hands = sysManager.GetEntitySystem<HandsSystem>();
        _production = sysManager.GetEntitySystem<NiiChemicalProductionSystem>();
        _steering = sysManager.GetEntitySystem<NPCSteeringSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(
        NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        var valid = TryGetOrder(blackboard, out _, out _, out var order) &&
                    order.Status is not (NiiProductionOrderStatus.Completed or
                        NiiProductionOrderStatus.Cancelled or NiiProductionOrderStatus.Blocked);
        return Task.FromResult<(bool, Dictionary<string, object>?)>((valid, null));
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!TryGetOrder(blackboard, out var technicianUid, out var orderUid, out var order))
            return Finish(blackboard, HTNOperatorStatus.Finished);

        if (order.Status == NiiProductionOrderStatus.Processing)
        {
            _steering.Unregister(technicianUid);
            return HTNOperatorStatus.Continuing;
        }

        if (order.Status is NiiProductionOrderStatus.Completed or
            NiiProductionOrderStatus.Cancelled or NiiProductionOrderStatus.Blocked)
            return Finish(blackboard, HTNOperatorStatus.Finished);

        if (order.CurrentSource is not { } sourceUid || _entityManager.Deleted(sourceUid))
        {
            _production.TryReserveNextInput((orderUid, order));
            return HTNOperatorStatus.Continuing;
        }

        if (!_hands.IsHolding(technicianUid, sourceUid))
        {
            if (!MoveIntoRange(technicianUid, sourceUid, out var sourceMovement))
                return sourceMovement == SteeringStatus.NoPath
                    ? Finish(blackboard, HTNOperatorStatus.Failed)
                    : HTNOperatorStatus.Continuing;

            _steering.Unregister(technicianUid);
            if (!_hands.TryPickupAnyHand(technicianUid, sourceUid))
                return HTNOperatorStatus.Continuing;
            order.Status = NiiProductionOrderStatus.DeliveringInput;
        }

        if (order.Reactor is not { } reactorUid || _entityManager.Deleted(reactorUid))
            return Finish(blackboard, HTNOperatorStatus.Failed);

        if (!MoveIntoRange(technicianUid, reactorUid, out var reactorMovement))
            return reactorMovement == SteeringStatus.NoPath
                ? Finish(blackboard, HTNOperatorStatus.Failed)
                : HTNOperatorStatus.Continuing;

        _steering.Unregister(technicianUid);
        if (!_production.TryLoadCurrentInput((orderUid, order), sourceUid, technicianUid))
            return HTNOperatorStatus.Failed;

        _hands.TryDrop(technicianUid, sourceUid);
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        if (blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entityManager))
            _steering.Unregister(owner);
    }

    private bool TryGetOrder(
        NPCBlackboard blackboard,
        out EntityUid technicianUid,
        out EntityUid orderUid,
        out NiiProductionOrderComponent order)
    {
        technicianUid = default;
        orderUid = default;
        order = default!;
        if (!blackboard.TryGetValue(NPCBlackboard.Owner, out technicianUid, _entityManager) ||
            !_entityManager.TryGetComponent<NiiEmployeeComponent>(technicianUid, out var employee) ||
            employee.Role != NiiEmployeeRole.LaboratoryTechnician ||
            employee.ActiveWorkOrder is not { } activeOrder ||
            !_entityManager.TryGetComponent<NiiProductionOrderComponent>(activeOrder, out var productionOrder) ||
            productionOrder.AssignedTo != technicianUid)
            return false;

        orderUid = activeOrder;
        order = productionOrder;
        return true;
    }

    private bool MoveIntoRange(EntityUid owner, EntityUid target, out SteeringStatus status)
    {
        var steering = _steering.Register(owner, _entityManager.GetComponent<TransformComponent>(target).Coordinates);
        steering.Range = WorkRange;
        status = steering.Status;
        return status == SteeringStatus.InRange;
    }

    private HTNOperatorStatus Finish(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        if (blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entityManager))
            _steering.Unregister(owner);
        return status;
    }
}
