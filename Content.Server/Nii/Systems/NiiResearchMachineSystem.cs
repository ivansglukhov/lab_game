using Content.Server.Nii.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Nii;
using Content.Shared.Nii.Prototypes;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Runs the physical half of NII research: reagent loading, timed work and result production.
/// </summary>
public sealed partial class NiiResearchMachineSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private NiiDirectorTerminalSystem _terminals = default!;
    [Dependency] private NiiResearchWorkOrderSystem _workOrders = default!;
    [Dependency] private NiiInstituteNarrativeSystem _narrative = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NiiResearchMachineComponent, AfterInteractUsingEvent>(OnInteractUsing);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NiiResearchMachineComponent>();
        while (query.MoveNext(out var uid, out var machine))
        {
            if (!machine.IsProcessing || machine.Institute is not { } instituteUid ||
                !TryComp<NiiInstituteComponent>(instituteUid, out var institute))
                continue;

            var project = _prototypes.Index(institute.ActiveProject);
            machine.ElapsedSeconds += frameTime;
            if (machine.ElapsedSeconds < project.DurationSeconds)
                continue;

            CompleteResearch(uid, machine, (instituteUid, institute), project);
        }
    }

    private void OnInteractUsing(
        Entity<NiiResearchMachineComponent> machine,
        ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        args.Handled = TryStartResearch(machine, args.Used, args.User);
    }

    public bool TryStartResearch(
        Entity<NiiResearchMachineComponent> machine,
        EntityUid sample,
        EntityUid user)
    {
        if (machine.Comp.IsProcessing)
            return false;

        var institutes = EntityQueryEnumerator<NiiInstituteComponent>();
        if (!institutes.MoveNext(out var instituteUid, out var institute) ||
            institute.ResearchStatus != NiiResearchStatus.Authorized)
        {
            _popup.PopupEntity(Loc.GetString("nii-research-machine-not-authorized"), machine, user);
            return false;
        }

        var project = _prototypes.Index(institute.ActiveProject);
        if (institute.ActiveWorkOrder is not { } workOrderUid ||
            !TryComp<NiiResearchWorkOrderComponent>(workOrderUid, out var workOrder) ||
            !_workOrders.TryBeginDelivery((workOrderUid, workOrder), sample, machine.Owner))
            return false;

        if (!_solutions.TryGetSolution(sample, machine.Comp.SolutionName, out var solutionEntity, out var solution) ||
            solution.GetTotalPrototypeQuantity(project.RequiredReagent) < project.RequiredReagentAmount)
        {
            _workOrders.Block((workOrderUid, workOrder), NiiWorkOrderBlockReason.NoSample);
            _terminals.RefreshAll(institute);
            _popup.PopupEntity(
                Loc.GetString("nii-research-machine-insufficient-reagent",
                    ("amount", project.RequiredReagentAmount.Int())),
                machine,
                user);
            return false;
        }

        _solutions.RemoveReagent(solutionEntity.Value, project.RequiredReagent, project.RequiredReagentAmount);
        machine.Comp.IsProcessing = true;
        machine.Comp.ElapsedSeconds = 0f;
        machine.Comp.Institute = instituteUid;
        institute.ResearchStatus = NiiResearchStatus.Running;
        _workOrders.MarkRunning((workOrderUid, workOrder));
        _narrative.Record(
            (instituteUid, institute),
            NiiInstituteEventType.ResearchStarted,
            NiiInstituteEventSeverity.Info,
            new NiiInstituteEventData(
                user,
                workOrderUid,
                machine.Comp.LaboratoryId,
                NiiWorkOrderStatus.Running));
        _terminals.RefreshAll(institute);
        _popup.PopupEntity(Loc.GetString("nii-research-machine-started"), machine, user);
        return true;
    }

    private void CompleteResearch(
        EntityUid uid,
        NiiResearchMachineComponent machine,
        Entity<NiiInstituteComponent> institute,
        NiiResearchProjectPrototype project)
    {
        machine.IsProcessing = false;
        machine.ElapsedSeconds = 0f;
        machine.Institute = null;
        institute.Comp.ResearchStatus = NiiResearchStatus.Completed;
        institute.Comp.Science += project.ScienceReward;
        institute.Comp.Reputation += project.ReputationReward;
        EntityUid? workOrderUid = institute.Comp.ActiveWorkOrder;
        EntityUid? assignedTo = null;
        if (workOrderUid is { } activeOrder &&
            TryComp<NiiResearchWorkOrderComponent>(activeOrder, out var activeWorkOrder))
            assignedTo = activeWorkOrder.AssignedTo;
        if (workOrderUid is { } completedOrderUid &&
            TryComp<NiiResearchWorkOrderComponent>(completedOrderUid, out var workOrder))
            _workOrders.Complete((completedOrderUid, workOrder));
        _narrative.Record(
            institute,
            NiiInstituteEventType.ResearchCompleted,
            NiiInstituteEventSeverity.Success,
            new NiiInstituteEventData(
                assignedTo,
                workOrderUid,
                machine.LaboratoryId,
                NiiWorkOrderStatus.Completed,
                Amount: project.ScienceReward));
        Spawn(project.ResultPrototype, Transform(uid).Coordinates);
        _terminals.RefreshAll(institute.Comp);
    }
}
