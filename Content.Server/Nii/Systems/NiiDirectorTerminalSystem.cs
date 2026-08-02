using Content.Server.Nii.Components;
using Content.Shared.Nii;
using Content.Shared.Nii.Components;
using Content.Shared.Nii.Prototypes;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Supplies authoritative institute data to all open director terminals.
/// </summary>
public sealed partial class NiiDirectorTerminalSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private NiiResearchWorkOrderSystem _workOrders = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NiiDirectorTerminalComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
        SubscribeLocalEvent<NiiDirectorTerminalComponent, NiiAuthorizeResearchMessage>(OnAuthorizeResearch);
        SubscribeLocalEvent<NiiResearchWorkOrderComponent, NiiWorkOrderChangedEvent>(OnWorkOrderChanged);
    }

    private void OnWorkOrderChanged(
        Entity<NiiResearchWorkOrderComponent> order,
        ref NiiWorkOrderChangedEvent args)
    {
        if (order.Comp.Institute is { } instituteUid &&
            TryComp<NiiInstituteComponent>(instituteUid, out var institute))
            RefreshAll(institute);
    }

    private void OnAuthorizeResearch(
        Entity<NiiDirectorTerminalComponent> terminal,
        ref NiiAuthorizeResearchMessage args)
    {
        if (args.Actor is not { Valid: true })
            return;

        var query = EntityQueryEnumerator<NiiInstituteComponent>();
        if (!query.MoveNext(out var instituteUid, out var institute))
            return;

        TryAuthorizeResearch((instituteUid, institute), args.Actor);
    }

    public bool TryAuthorizeResearch(
        Entity<NiiInstituteComponent> institute,
        EntityUid? requestedBy = null)
    {
        var project = _prototypes.Index(institute.Comp.ActiveProject);
        if (institute.Comp.ResearchStatus != NiiResearchStatus.Available ||
            institute.Comp.Balance < project.Cost)
            return false;

        if (_workOrders.CreateAndAssign(institute, requestedBy) is null)
            return false;

        institute.Comp.Balance -= project.Cost;
        institute.Comp.ResearchStatus = NiiResearchStatus.Authorized;
        institute.Comp.IsBankrupt = institute.Comp.Balance < 0;
        NiiInstituteSystem.AddEvent(institute.Comp, NiiInstituteEventType.ProjectAuthorized);
        RefreshAll(institute.Comp);
        return true;
    }

    private void OnBeforeUiOpen(
        Entity<NiiDirectorTerminalComponent> terminal,
        ref BeforeActivatableUIOpenEvent args)
    {
        var query = EntityQueryEnumerator<NiiInstituteComponent>();
        if (query.MoveNext(out _, out var institute))
            SetState(terminal, institute);
    }

    public void RefreshAll(NiiInstituteComponent institute)
    {
        var query = EntityQueryEnumerator<NiiDirectorTerminalComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            SetState(uid, institute);
        }
    }

    private void SetState(EntityUid terminal, NiiInstituteComponent institute)
    {
        var project = _prototypes.Index(institute.ActiveProject);
        var workOrderUid = institute.ActiveWorkOrder ?? institute.LastWorkOrder;
        var hasWorkOrder = TryComp<NiiResearchWorkOrderComponent>(workOrderUid, out var workOrder);
        var assignedEmployeeName = workOrder?.AssignedTo is { } employeeUid && !Deleted(employeeUid)
            ? MetaData(employeeUid).EntityName
            : string.Empty;
        _ui.SetUiState(
            terminal,
            NiiDirectorTerminalUiKey.Key,
            new NiiDirectorTerminalBuiState(
                institute.CurrentDay,
                institute.Balance,
                institute.DailyFunding,
                institute.DailyExpenses,
                institute.Reputation,
                institute.Science,
                institute.IsBankrupt,
                Loc.GetString(project.Name),
                project.Cost,
                project.RequiredReagentAmount.Int(),
                (int) project.DurationSeconds,
                institute.ResearchStatus,
                institute.ResearchStatus == NiiResearchStatus.Available && institute.Balance >= project.Cost,
                hasWorkOrder,
                workOrder?.Status ?? NiiWorkOrderStatus.Created,
                workOrder?.BlockReason ?? NiiWorkOrderBlockReason.None,
                assignedEmployeeName,
                institute.EventLog.ToArray()));
    }
}
