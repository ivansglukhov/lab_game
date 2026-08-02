using Content.Server.Nii.Components;
using Content.Shared.Nii;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Connects map-authored laboratories, employees and machines to the round's institute state.
/// </summary>
public sealed partial class NiiLaboratorySystem : EntitySystem
{
    private bool _reconcileQueued;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NiiLaboratoryComponent, MapInitEvent>(OnMapChanged);
        SubscribeLocalEvent<NiiLaboratoryComponent, ComponentShutdown>(OnMapChanged);
        SubscribeLocalEvent<NiiEmployeeComponent, MapInitEvent>(OnMapChanged);
        SubscribeLocalEvent<NiiEmployeeComponent, ComponentShutdown>(OnMapChanged);
        SubscribeLocalEvent<NiiResearchMachineComponent, MapInitEvent>(OnMapChanged);
        SubscribeLocalEvent<NiiResearchMachineComponent, ComponentShutdown>(OnMapChanged);
        SubscribeLocalEvent<NiiInstituteComponent, ComponentShutdown>(OnMapChanged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (!_reconcileQueued)
            return;

        _reconcileQueued = false;
        ReconcileNow();
    }

    private void OnMapChanged(Entity<NiiLaboratoryComponent> entity, ref MapInitEvent args)
    {
        _reconcileQueued = true;
    }

    private void OnMapChanged(Entity<NiiLaboratoryComponent> entity, ref ComponentShutdown args)
    {
        _reconcileQueued = true;
    }

    private void OnMapChanged(Entity<NiiEmployeeComponent> entity, ref MapInitEvent args)
    {
        _reconcileQueued = true;
    }

    private void OnMapChanged(Entity<NiiEmployeeComponent> entity, ref ComponentShutdown args)
    {
        _reconcileQueued = true;
    }

    private void OnMapChanged(Entity<NiiResearchMachineComponent> entity, ref MapInitEvent args)
    {
        _reconcileQueued = true;
    }

    private void OnMapChanged(Entity<NiiResearchMachineComponent> entity, ref ComponentShutdown args)
    {
        _reconcileQueued = true;
    }

    private void OnMapChanged(Entity<NiiInstituteComponent> entity, ref ComponentShutdown args)
    {
        _reconcileQueued = true;
    }

    public void ReconcileNow()
    {
        var laboratories = new Dictionary<string, Entity<NiiLaboratoryComponent>>(StringComparer.Ordinal);
        var laboratoryQuery = EntityQueryEnumerator<NiiLaboratoryComponent>();
        while (laboratoryQuery.MoveNext(out var uid, out var laboratory))
        {
            laboratory.Institute = null;
            laboratory.Head = null;
            laboratory.Researchers.Clear();
            laboratory.ResearchMachine = null;
            laboratories.TryAdd(laboratory.LaboratoryId, (uid, laboratory));
        }

        var employeeQuery = EntityQueryEnumerator<NiiEmployeeComponent>();
        while (employeeQuery.MoveNext(out var uid, out var employee))
        {
            employee.Laboratory = null;
            if (!laboratories.TryGetValue(employee.LaboratoryId, out var laboratory))
                continue;

            employee.Laboratory = laboratory.Owner;
            if (employee.Role == NiiEmployeeRole.LaboratoryHead)
                laboratory.Comp.Head = uid;
            else
                laboratory.Comp.Researchers.Add(uid);
        }

        var machineQuery = EntityQueryEnumerator<NiiResearchMachineComponent>();
        while (machineQuery.MoveNext(out var uid, out var machine))
        {
            if (laboratories.TryGetValue(machine.LaboratoryId, out var laboratory))
                laboratory.Comp.ResearchMachine = uid;
        }

        var instituteQuery = EntityQueryEnumerator<NiiInstituteComponent>();
        if (!instituteQuery.MoveNext(out var instituteUid, out var institute))
            return;

        institute.Laboratories.Clear();
        foreach (var laboratory in laboratories.Values)
        {
            laboratory.Comp.Institute = instituteUid;
            institute.Laboratories.Add(laboratory.Owner);
        }
    }
}
