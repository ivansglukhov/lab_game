using Content.Server.Nii.Components;
using Content.Shared.Nii;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Owns the management-level lifecycle of research work orders.
/// Physical NPC actions will drive the intermediate transitions in a later slice.
/// </summary>
public sealed partial class NiiResearchWorkOrderSystem : EntitySystem
{
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

    public EntityUid? CreateAndAssign(
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

            institute.Comp.ActiveWorkOrder = orderUid;
            laboratory.ActiveWorkOrder = orderUid;
            TryAssign((orderUid, order), (laboratoryUid, laboratory));
            return orderUid;
        }

        return null;
    }

    public bool TryAssign(
        Entity<NiiResearchWorkOrderComponent> order,
        Entity<NiiLaboratoryComponent> laboratory)
    {
        if (order.Comp.Status is NiiWorkOrderStatus.Completed or NiiWorkOrderStatus.Cancelled)
            return false;

        order.Comp.Status = NiiWorkOrderStatus.AwaitingAssignment;
        order.Comp.BlockReason = NiiWorkOrderBlockReason.None;

        foreach (var researcherUid in laboratory.Comp.Researchers)
        {
            if (!TryComp<NiiEmployeeComponent>(researcherUid, out var researcher) ||
                researcher.ActiveWorkOrder is { } employeeOrder && !Deleted(employeeOrder))
                continue;

            researcher.ActiveWorkOrder = null;
            order.Comp.AssignedTo = researcherUid;
            researcher.ActiveWorkOrder = order.Owner;
            order.Comp.Status = NiiWorkOrderStatus.Assigned;

            if (laboratory.Comp.ResearchMachine is not { } machineUid || Deleted(machineUid))
            {
                Block(order, NiiWorkOrderBlockReason.MachineInaccessible);
                return false;
            }

            order.Comp.Machine = machineUid;
            return TryReserveSample(order);
        }

        Block(order, NiiWorkOrderBlockReason.EmployeeUnavailable);
        return false;
    }

    public bool TryReserveSample(Entity<NiiResearchWorkOrderComponent> order)
    {
        var sampleQuery = EntityQueryEnumerator<NiiResearchSampleComponent>();
        while (sampleQuery.MoveNext(out var sampleUid, out _))
        {
            if (order.Comp.Laboratory is { } laboratoryUid &&
                Transform(sampleUid).MapID != Transform(laboratoryUid).MapID)
                continue;

            order.Comp.Sample = sampleUid;
            order.Comp.BlockReason = NiiWorkOrderBlockReason.None;
            order.Comp.Status = NiiWorkOrderStatus.FetchingSample;
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

        order.Comp.Sample = sample;
        order.Comp.Machine = machine;
        order.Comp.BlockReason = NiiWorkOrderBlockReason.None;
        order.Comp.Status = NiiWorkOrderStatus.DeliveringSample;
        NotifyChanged(order);
        return true;
    }

    public void MarkRunning(Entity<NiiResearchWorkOrderComponent> order)
    {
        order.Comp.BlockReason = NiiWorkOrderBlockReason.None;
        order.Comp.Status = NiiWorkOrderStatus.Running;
        NotifyChanged(order);
    }

    public void Complete(Entity<NiiResearchWorkOrderComponent> order)
    {
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
        order.Comp.Status = NiiWorkOrderStatus.Blocked;
        order.Comp.BlockReason = reason;
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
}

[ByRefEvent]
public record struct NiiWorkOrderChangedEvent;
