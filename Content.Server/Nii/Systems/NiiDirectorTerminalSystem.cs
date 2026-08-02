using System.Linq;
using Content.Server.Nii.Components;
using Content.Shared.Mobs;
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
    [Dependency] private NiiInstituteNarrativeSystem _narrative = default!;
    [Dependency] private NiiInstituteChatSystem _chat = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NiiDirectorTerminalComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
        SubscribeLocalEvent<NiiDirectorTerminalComponent, NiiAuthorizeResearchMessage>(OnAuthorizeResearch);
        SubscribeLocalEvent<NiiDirectorTerminalComponent, NiiAssignResearcherMessage>(OnAssignResearcher);
        SubscribeLocalEvent<NiiDirectorTerminalComponent, NiiSetDelegatedAssignmentMessage>(OnSetDelegatedAssignment);
        SubscribeLocalEvent<NiiResearchWorkOrderComponent, NiiWorkOrderChangedEvent>(OnWorkOrderChanged);
        SubscribeLocalEvent<NiiEmployeeComponent, MobStateChangedEvent>(OnEmployeeMobStateChanged);
    }

    private void OnEmployeeMobStateChanged(
        Entity<NiiEmployeeComponent> employee,
        ref MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Alive &&
            employee.Comp.ActiveWorkOrder is { } orderUid &&
            TryComp<NiiResearchWorkOrderComponent>(orderUid, out var order) &&
            order.Status is not (NiiWorkOrderStatus.Completed or NiiWorkOrderStatus.Cancelled))
            _workOrders.Block((orderUid, order), NiiWorkOrderBlockReason.EmployeeUnavailable);

        if (employee.Comp.Laboratory is { } laboratoryUid &&
            TryComp<NiiLaboratoryComponent>(laboratoryUid, out var laboratory) &&
            laboratory.Institute is { } instituteUid &&
            TryComp<NiiInstituteComponent>(instituteUid, out var institute))
        {
            if (args.OldMobState == MobState.Alive && args.NewMobState != MobState.Alive)
            {
                _narrative.Record(
                    (instituteUid, institute),
                    NiiInstituteEventType.EmployeeUnavailable,
                    NiiInstituteEventSeverity.Critical,
                    new NiiInstituteEventData(
                        employee.Owner,
                        employee.Comp.ActiveWorkOrder,
                        laboratory.LaboratoryId,
                        BlockReason: NiiWorkOrderBlockReason.EmployeeUnavailable));
            }
            RefreshAll(institute);
        }
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

        _chat.SendDirectorCommand(args.Actor, Loc.GetString("nii-command-authorize-project"));
        TryAuthorizeResearch((instituteUid, institute), args.Actor);
    }

    private void OnAssignResearcher(
        Entity<NiiDirectorTerminalComponent> terminal,
        ref NiiAssignResearcherMessage args)
    {
        if (args.Actor is not { Valid: true } ||
            !TryGetEntity(args.Employee, out var employeeUid) ||
            !TryGetInstitute(out var instituteUid, out var institute))
            return;

        _chat.SendDirectorCommand(
            args.Actor,
            Loc.GetString("nii-command-assign-researcher", ("employee", MetaData(employeeUid.Value).EntityName)));
        TryAssignResearcher((instituteUid, institute), employeeUid.Value);
    }

    private void OnSetDelegatedAssignment(
        Entity<NiiDirectorTerminalComponent> terminal,
        ref NiiSetDelegatedAssignmentMessage args)
    {
        if (args.Actor is not { Valid: true } ||
            !TryGetInstitute(out var instituteUid, out var institute))
            return;

        _chat.SendDirectorCommand(
            args.Actor,
            Loc.GetString(args.Enabled
                ? "nii-command-enable-delegation"
                : "nii-command-disable-delegation"));
        TrySetDelegatedAssignment((instituteUid, institute), args.Enabled, args.Actor);
    }

    public bool TryAssignResearcher(
        Entity<NiiInstituteComponent> institute,
        EntityUid employeeUid)
    {
        if (institute.Comp.ActiveWorkOrder is not { } orderUid ||
            !TryComp<NiiResearchWorkOrderComponent>(orderUid, out var order) ||
            order.Laboratory is not { } laboratoryUid ||
            !TryComp<NiiLaboratoryComponent>(laboratoryUid, out var laboratory))
            return false;

        return _workOrders.TryAssign((orderUid, order), (laboratoryUid, laboratory), employeeUid);
    }

    public bool TrySetDelegatedAssignment(
        Entity<NiiInstituteComponent> institute,
        bool enabled,
        EntityUid? requestedBy = null)
    {
        if (!TryGetPrimaryLaboratory(institute.Comp, out var laboratoryUid, out var laboratory))
            return false;

        if (enabled &&
            (laboratory.Head is not { } headUid ||
             _workOrders.GetAvailability(headUid) != NiiEmployeeAvailability.Available))
            return false;

        var newMode = enabled
            ? NiiLaboratoryAssignmentMode.Delegated
            : NiiLaboratoryAssignmentMode.Manual;
        var modeChanged = laboratory.AssignmentMode != newMode;
        laboratory.AssignmentMode = newMode;

        if (modeChanged)
        {
            _narrative.Record(
                institute,
                NiiInstituteEventType.AssignmentModeChanged,
                NiiInstituteEventSeverity.Info,
                new NiiInstituteEventData(
                    requestedBy,
                    LaboratoryId: laboratory.LaboratoryId,
                    AssignmentMode: newMode));
        }

        if (enabled &&
            laboratory.ActiveWorkOrder is { } orderUid &&
            TryComp<NiiResearchWorkOrderComponent>(orderUid, out var order))
            _workOrders.TryAssignDelegated((orderUid, order), (laboratoryUid, laboratory));

        RefreshAll(institute.Comp);
        return true;
    }

    public bool TryAuthorizeResearch(
        Entity<NiiInstituteComponent> institute,
        EntityUid? requestedBy = null)
    {
        var project = _prototypes.Index(institute.Comp.ActiveProject);
        if (institute.Comp.ResearchStatus != NiiResearchStatus.Available ||
            institute.Comp.Balance < project.Cost)
            return false;

        var orderUid = _workOrders.Create(institute, requestedBy);
        if (orderUid is null)
            return false;

        institute.Comp.Balance -= project.Cost;
        institute.Comp.ResearchStatus = NiiResearchStatus.Authorized;
        institute.Comp.IsBankrupt = institute.Comp.Balance < 0;
        _narrative.Record(
            institute,
            NiiInstituteEventType.ProjectAuthorized,
            NiiInstituteEventSeverity.Info,
            new NiiInstituteEventData(requestedBy, orderUid, Amount: project.Cost));

        if (TryComp<NiiResearchWorkOrderComponent>(orderUid.Value, out var order) &&
            order.Laboratory is { } laboratoryUid &&
            TryComp<NiiLaboratoryComponent>(laboratoryUid, out var laboratory))
            _workOrders.TryAssignDelegated((orderUid.Value, order), (laboratoryUid, laboratory));

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
        var hasLaboratory = TryGetPrimaryLaboratory(institute, out _, out var laboratory);
        var headName = string.Empty;
        var headAvailability = NiiEmployeeAvailability.Unavailable;
        var assignmentMode = NiiLaboratoryAssignmentMode.Manual;
        var researchers = Array.Empty<NiiEmployeeUiState>();

        if (hasLaboratory)
        {
            assignmentMode = laboratory.AssignmentMode;
            if (laboratory.Head is { } headUid && !Deleted(headUid))
            {
                headName = MetaData(headUid).EntityName;
                headAvailability = _workOrders.GetAvailability(headUid);
            }

            researchers = laboratory.Researchers
                .Where(uid => !Deleted(uid) && TryComp<NiiEmployeeComponent>(uid, out _))
                .Select(uid => new NiiEmployeeUiState(
                    GetNetEntity(uid),
                    MetaData(uid).EntityName,
                    NiiEmployeeRole.Researcher,
                    _workOrders.GetAvailability(uid)))
                .OrderBy(employee => employee.Name, StringComparer.CurrentCulture)
                .ToArray();
        }

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
                hasLaboratory,
                headName,
                headAvailability,
                assignmentMode,
                researchers));
    }

    private bool TryGetInstitute(out EntityUid uid, out NiiInstituteComponent institute)
    {
        var query = EntityQueryEnumerator<NiiInstituteComponent>();
        if (query.MoveNext(out uid, out var component))
        {
            institute = component;
            return true;
        }

        institute = default!;
        return false;
    }

    private bool TryGetPrimaryLaboratory(
        NiiInstituteComponent institute,
        out EntityUid uid,
        out NiiLaboratoryComponent laboratory)
    {
        foreach (var laboratoryUid in institute.Laboratories)
        {
            if (!TryComp<NiiLaboratoryComponent>(laboratoryUid, out var candidate))
                continue;

            uid = laboratoryUid;
            laboratory = candidate;
            return true;
        }

        uid = default;
        laboratory = default!;
        return false;
    }
}
