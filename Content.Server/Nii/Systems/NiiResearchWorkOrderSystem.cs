using System.Linq;
using Content.Server.Nii.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Nii;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Owns the management-level lifecycle of research work orders.
/// Physical NPC actions will drive the intermediate transitions in a later slice.
/// </summary>
public sealed partial class NiiResearchWorkOrderSystem : EntitySystem
{
    [Dependency] private NiiInstituteNarrativeSystem _narrative = default!;

    private bool _sampleReconciliationQueued;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NiiResearchSampleComponent, MapInitEvent>(OnSampleChanged);
        SubscribeLocalEvent<NiiResearchSampleComponent, ComponentShutdown>(OnSampleChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_sampleReconciliationQueued)
            return;

        _sampleReconciliationQueued = false;
        ReconcileSamples();
    }

    private void OnSampleChanged(Entity<NiiResearchSampleComponent> entity, ref MapInitEvent args)
    {
        _sampleReconciliationQueued = true;
    }

    private void OnSampleChanged(Entity<NiiResearchSampleComponent> entity, ref ComponentShutdown args)
    {
        _sampleReconciliationQueued = true;
    }

    public EntityUid? Create(
        Entity<NiiInstituteComponent> institute,
        EntityUid? requestedBy = null)
    {
        if (institute.Comp.ActiveWorkOrder is { } activeOrder && !Deleted(activeOrder))
            return null;

        institute.Comp.ActiveWorkOrder = null;

        foreach (var laboratoryUid in institute.Comp.Laboratories)
        {
            if (!TryComp<NiiLaboratoryComponent>(laboratoryUid, out var laboratory) ||
                laboratory.ActiveWorkOrder is { } laboratoryOrder && !Deleted(laboratoryOrder))
                continue;

            laboratory.ActiveWorkOrder = null;

            var orderUid = Spawn("NiiResearchWorkOrder", Transform(laboratoryUid).Coordinates);
            var order = Comp<NiiResearchWorkOrderComponent>(orderUid);
            order.Project = institute.Comp.ActiveProject;
            order.Institute = institute.Owner;
            order.Laboratory = laboratoryUid;
            order.RequestedBy = requestedBy;
            order.Status = NiiWorkOrderStatus.AwaitingProduction;

            institute.Comp.ActiveWorkOrder = orderUid;
            laboratory.ActiveWorkOrder = orderUid;
            RecordOrderEvent(
                (orderUid, order),
                NiiInstituteEventType.WorkOrderCreated,
                NiiInstituteEventSeverity.Info,
                requestedBy);
            NotifyChanged((orderUid, order));
            return orderUid;
        }

        return null;
    }

    public bool TryAssign(
        Entity<NiiResearchWorkOrderComponent> order,
        Entity<NiiLaboratoryComponent> laboratory,
        EntityUid researcherUid)
    {
        if (order.Comp.Status != NiiWorkOrderStatus.AwaitingAssignment ||
            order.Comp.Laboratory != laboratory.Owner ||
            !laboratory.Comp.Researchers.Contains(researcherUid) ||
            !TryComp<NiiEmployeeComponent>(researcherUid, out var researcher) ||
            researcher.Role != NiiEmployeeRole.Researcher ||
            researcher.Laboratory != laboratory.Owner ||
            GetAvailability(researcherUid, researcher) != NiiEmployeeAvailability.Available)
            return false;

        order.Comp.BlockReason = NiiWorkOrderBlockReason.None;
        if (researcher.ActiveWorkOrder is { } staleOrder && Deleted(staleOrder))
            researcher.ActiveWorkOrder = null;

        order.Comp.AssignedTo = researcherUid;
        researcher.ActiveWorkOrder = order.Owner;
        order.Comp.Status = NiiWorkOrderStatus.Assigned;
        RecordOrderEvent(
            order,
            NiiInstituteEventType.EmployeeAssigned,
            NiiInstituteEventSeverity.Info,
            researcherUid);

        if (laboratory.Comp.ResearchMachine is not { } machineUid || Deleted(machineUid))
        {
            Block(order, NiiWorkOrderBlockReason.MachineInaccessible);
            return true;
        }

        order.Comp.Machine = machineUid;
        if (order.Comp.Sample is { } sampleUid && !Deleted(sampleUid) && HasComp<NiiResearchSampleComponent>(sampleUid))
        {
            order.Comp.Status = NiiWorkOrderStatus.FetchingSample;
            RecordOrderEvent(
                order,
                NiiInstituteEventType.SampleReserved,
                NiiInstituteEventSeverity.Info,
                researcherUid);
            NotifyChanged(order);
        }
        else
        {
            Block(order, NiiWorkOrderBlockReason.ProductionUnavailable);
        }
        return true;
    }

    public void OnProductionCompleted(
        Entity<NiiResearchWorkOrderComponent> order,
        EntityUid sampleUid)
    {
        if (order.Comp.Status is NiiWorkOrderStatus.Completed or NiiWorkOrderStatus.Cancelled)
            return;

        order.Comp.Sample = sampleUid;
        order.Comp.BlockReason = NiiWorkOrderBlockReason.None;
        order.Comp.Status = NiiWorkOrderStatus.AwaitingAssignment;
        NotifyChanged(order);

        if (order.Comp.Laboratory is { } laboratoryUid &&
            TryComp<NiiLaboratoryComponent>(laboratoryUid, out var laboratory))
            TryAssignDelegated(order, (laboratoryUid, laboratory));
    }

    public bool TryAssignDelegated(
        Entity<NiiResearchWorkOrderComponent> order,
        Entity<NiiLaboratoryComponent> laboratory)
    {
        if (laboratory.Comp.AssignmentMode != NiiLaboratoryAssignmentMode.Delegated ||
            laboratory.Comp.Head is not { } headUid ||
            GetAvailability(headUid) != NiiEmployeeAvailability.Available)
            return false;

        foreach (var researcherUid in laboratory.Comp.Researchers.OrderBy(uid => uid))
        {
            if (GetAvailability(researcherUid) == NiiEmployeeAvailability.Available)
                return TryAssign(order, laboratory, researcherUid);
        }

        return false;
    }

    public NiiEmployeeAvailability GetAvailability(
        EntityUid employeeUid,
        NiiEmployeeComponent? employee = null)
    {
        if (!Resolve(employeeUid, ref employee, false) ||
            !TryComp<MobStateComponent>(employeeUid, out var mobState) ||
            mobState.CurrentState != MobState.Alive)
            return NiiEmployeeAvailability.Unavailable;

        return employee.ActiveWorkOrder is { } activeOrder && !Deleted(activeOrder)
            ? NiiEmployeeAvailability.Busy
            : NiiEmployeeAvailability.Available;
    }

    public bool TryReserveSample(Entity<NiiResearchWorkOrderComponent> order)
    {
        var wasBlocked = order.Comp.Status == NiiWorkOrderStatus.Blocked;
        var sampleQuery = EntityQueryEnumerator<NiiResearchSampleComponent>();
        while (sampleQuery.MoveNext(out var sampleUid, out _))
        {
            if (order.Comp.Laboratory is { } laboratoryUid &&
                Transform(sampleUid).MapID != Transform(laboratoryUid).MapID)
                continue;

            order.Comp.Sample = sampleUid;
            order.Comp.BlockReason = NiiWorkOrderBlockReason.None;
            order.Comp.Status = NiiWorkOrderStatus.FetchingSample;
            if (wasBlocked)
            {
                RecordOrderEvent(
                    order,
                    NiiInstituteEventType.WorkOrderRecovered,
                    NiiInstituteEventSeverity.Info,
                    order.Comp.AssignedTo);
            }
            RecordOrderEvent(
                order,
                NiiInstituteEventType.SampleReserved,
                NiiInstituteEventSeverity.Info,
                order.Comp.AssignedTo);
            NotifyChanged(order);
            return true;
        }

        Block(order, NiiWorkOrderBlockReason.NoSample);
        return false;
    }

    public bool TryBeginDelivery(
        Entity<NiiResearchWorkOrderComponent> order,
        EntityUid sample,
        EntityUid machine)
    {
        if (order.Comp.Status is NiiWorkOrderStatus.Completed or NiiWorkOrderStatus.Cancelled ||
            order.Comp.AssignedTo is not { Valid: true })
            return false;

        if (order.Comp.Status == NiiWorkOrderStatus.DeliveringSample &&
            order.Comp.Sample == sample &&
            order.Comp.Machine == machine)
            return true;

        order.Comp.Sample = sample;
        order.Comp.Machine = machine;
        order.Comp.BlockReason = NiiWorkOrderBlockReason.None;
        order.Comp.Status = NiiWorkOrderStatus.DeliveringSample;
        RecordOrderEvent(
            order,
            NiiInstituteEventType.SampleDeliveryStarted,
            NiiInstituteEventSeverity.Info,
            order.Comp.AssignedTo);
        NotifyChanged(order);
        return true;
    }

    public void MarkRunning(Entity<NiiResearchWorkOrderComponent> order)
    {
        if (order.Comp.Status == NiiWorkOrderStatus.Running)
            return;

        order.Comp.BlockReason = NiiWorkOrderBlockReason.None;
        order.Comp.Status = NiiWorkOrderStatus.Running;
        NotifyChanged(order);
    }

    public void Complete(Entity<NiiResearchWorkOrderComponent> order)
    {
        if (order.Comp.Status == NiiWorkOrderStatus.Completed)
            return;

        order.Comp.BlockReason = NiiWorkOrderBlockReason.None;
        order.Comp.Status = NiiWorkOrderStatus.Completed;

        if (order.Comp.AssignedTo is { } employeeUid &&
            TryComp<NiiEmployeeComponent>(employeeUid, out var employee) &&
            employee.ActiveWorkOrder == order.Owner)
            employee.ActiveWorkOrder = null;

        if (order.Comp.Laboratory is { } laboratoryUid &&
            TryComp<NiiLaboratoryComponent>(laboratoryUid, out var laboratory) &&
            laboratory.ActiveWorkOrder == order.Owner)
            laboratory.ActiveWorkOrder = null;

        if (order.Comp.Institute is { } instituteUid &&
            TryComp<NiiInstituteComponent>(instituteUid, out var institute) &&
            institute.ActiveWorkOrder == order.Owner)
        {
            institute.ActiveWorkOrder = null;
            institute.LastWorkOrder = order.Owner;
        }

        NotifyChanged(order);
    }

    public void Block(
        Entity<NiiResearchWorkOrderComponent> order,
        NiiWorkOrderBlockReason reason)
    {
        if (order.Comp.Status == NiiWorkOrderStatus.Blocked && order.Comp.BlockReason == reason)
            return;

        order.Comp.Status = NiiWorkOrderStatus.Blocked;
        order.Comp.BlockReason = reason;
        RecordOrderEvent(
            order,
            reason == NiiWorkOrderBlockReason.NoSample
                ? NiiInstituteEventType.ResourceShortage
                : NiiInstituteEventType.WorkOrderBlocked,
            reason == NiiWorkOrderBlockReason.EmployeeUnavailable
                ? NiiInstituteEventSeverity.Critical
                : NiiInstituteEventSeverity.Attention,
            order.Comp.AssignedTo);
        NotifyChanged(order);
    }

    private void ReconcileSamples()
    {
        var orderQuery = EntityQueryEnumerator<NiiResearchWorkOrderComponent>();
        while (orderQuery.MoveNext(out var orderUid, out var order))
        {
            if (order.Status is NiiWorkOrderStatus.Completed or NiiWorkOrderStatus.Cancelled or
                NiiWorkOrderStatus.Running or NiiWorkOrderStatus.DeliveringSample ||
                order.Status == NiiWorkOrderStatus.Blocked && order.BlockReason != NiiWorkOrderBlockReason.NoSample)
                continue;

            if (order.Sample is { } sampleUid && !Deleted(sampleUid) && HasComp<NiiResearchSampleComponent>(sampleUid))
                continue;

            order.Sample = null;
            TryReserveSample((orderUid, order));
        }
    }

    private void NotifyChanged(Entity<NiiResearchWorkOrderComponent> order)
    {
        var ev = new NiiWorkOrderChangedEvent();
        RaiseLocalEvent(order.Owner, ref ev);
    }

    private void RecordOrderEvent(
        Entity<NiiResearchWorkOrderComponent> order,
        NiiInstituteEventType type,
        NiiInstituteEventSeverity severity,
        EntityUid? actor)
    {
        if (order.Comp.Institute is not { } instituteUid ||
            !TryComp<NiiInstituteComponent>(instituteUid, out var institute))
            return;

        var laboratoryId = order.Comp.Laboratory is { } laboratoryUid &&
                           TryComp<NiiLaboratoryComponent>(laboratoryUid, out var laboratory)
            ? laboratory.LaboratoryId
            : string.Empty;
        _narrative.Record(
            (instituteUid, institute),
            type,
            severity,
            new NiiInstituteEventData(
                actor,
                order.Owner,
                laboratoryId,
                order.Comp.Status,
                order.Comp.BlockReason));
    }
}

[ByRefEvent]
public record struct NiiWorkOrderChangedEvent;
