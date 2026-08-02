using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Nii.Components;
using Content.Server.Nii.Systems;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Gravity;
using Content.Shared.Nii;
using Content.Shared.Nii.Prototypes;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.Nii.AI;

/// <summary>
/// Builds a bounded, read-only observation from authoritative simulation components.
/// </summary>
public sealed partial class NiiInstituteAiContextSystem : EntitySystem
{
    public const string Authority = "observe_only";
    public const int MaximumRecentEvents = 12;

    private static readonly JsonSerializerOptions SerializerOptions = CreateSerializerOptions();

    [Dependency] private AtmosphereSystem _atmosphere = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private NiiResearchWorkOrderSystem _workOrders = default!;

    public NiiInstituteAiContext Build(Entity<NiiInstituteComponent> institute)
    {
        var project = _prototypes.Index(institute.Comp.ActiveProject);
        var laboratories = new List<NiiAiLaboratoryContext>();
        foreach (var laboratoryUid in institute.Comp.Laboratories)
        {
            if (!TryComp<NiiLaboratoryComponent>(laboratoryUid, out var laboratory))
                continue;

            var employees = new List<NiiAiEmployeeContext>();
            if (laboratory.Head is { } headUid && TryComp<NiiEmployeeComponent>(headUid, out var head))
                employees.Add(EmployeeContext(headUid, head));
            foreach (var researcherUid in laboratory.Researchers.OrderBy(uid => uid))
            {
                if (TryComp<NiiEmployeeComponent>(researcherUid, out var researcher))
                    employees.Add(EmployeeContext(researcherUid, researcher));
            }

            NiiAiWorkOrderContext? orderContext = null;
            if (laboratory.ActiveWorkOrder is { } orderUid &&
                TryComp<NiiResearchWorkOrderComponent>(orderUid, out var order))
            {
                var reagentAvailable = 0f;
                var solutionName = order.Machine is { } orderMachineUid &&
                                   TryComp<NiiResearchMachineComponent>(orderMachineUid, out var orderMachine)
                    ? orderMachine.SolutionName
                    : "beaker";
                if (order.Sample is { } sampleUid &&
                    _solutions.TryGetSolution(sampleUid, solutionName, out _, out var solution))
                    reagentAvailable = solution.GetTotalPrototypeQuantity(project.RequiredReagent).Float();

                orderContext = new NiiAiWorkOrderContext(
                    PublicId(orderUid, "order"),
                    order.Status,
                    order.BlockReason,
                    PublicId(order.AssignedTo, "actor"),
                    PublicId(order.Sample, "sample"),
                    reagentAvailable);
            }

            NiiAiMachineContext? machineContext = null;
            if (laboratory.ResearchMachine is { } machineUid &&
                TryComp<NiiResearchMachineComponent>(machineUid, out var machine))
            {
                var progress = machine.IsProcessing && project.DurationSeconds > 0f
                    ? Math.Clamp(machine.ElapsedSeconds / project.DurationSeconds, 0f, 1f)
                    : 0f;
                machineContext = new NiiAiMachineContext(
                    PublicId(machineUid, "machine"),
                    machine.IsProcessing,
                    progress);
            }

            laboratories.Add(new NiiAiLaboratoryContext(
                laboratory.LaboratoryId,
                laboratory.AssignmentMode,
                employees.ToArray(),
                orderContext,
                machineContext,
                SensorContext(laboratoryUid)));
        }

        return new NiiInstituteAiContext(
            NiiInstituteNarrativeSystem.SchemaVersion,
            Authority,
            institute.Comp.CurrentDay,
            SecondsIntoDay(institute.Comp),
            institute.Comp.Balance,
            institute.Comp.DailyFunding,
            institute.Comp.DailyExpenses,
            institute.Comp.Reputation,
            institute.Comp.Science,
            institute.Comp.IsBankrupt,
            $"{institute.Comp.ActiveProject}",
            institute.Comp.ResearchStatus,
            laboratories.ToArray(),
            institute.Comp.EventLog.TakeLast(MaximumRecentEvents).ToArray());
    }

    public string Serialize(NiiInstituteAiContext context)
    {
        return JsonSerializer.Serialize(context, SerializerOptions);
    }

    private NiiAiEmployeeContext EmployeeContext(EntityUid uid, NiiEmployeeComponent employee)
    {
        return new NiiAiEmployeeContext(
            PublicId(uid, "actor"),
            MetaData(uid).EntityName,
            employee.Role,
            _workOrders.GetAvailability(uid, employee),
            PublicId(employee.ActiveWorkOrder, "order"));
    }

    private NiiAiSensorContext SensorContext(EntityUid laboratoryUid)
    {
        var mixture = _atmosphere.GetContainingMixture(laboratoryUid, true);
        var transform = Transform(laboratoryUid);
        var gravityEnabled = transform.GridUid is { } gridUid &&
                             TryComp<GravityComponent>(gridUid, out var gravity) &&
                             gravity.Enabled;
        var enabledLights = 0;
        if (transform.GridUid is { } lightGridUid)
        {
            var lights = EntityQueryEnumerator<PointLightComponent, TransformComponent>();
            while (lights.MoveNext(out _, out var light, out var lightTransform))
            {
                if (light.Enabled && lightTransform.GridUid == lightGridUid)
                    enabledLights++;
            }
        }

        return new NiiAiSensorContext(
            mixture is not null,
            mixture?.Pressure ?? 0f,
            mixture?.Temperature ?? 0f,
            mixture?.GetMoles(Gas.Oxygen) ?? 0f,
            mixture?.GetMoles(Gas.Plasma) ?? 0f,
            mixture?.GetMoles(Gas.CarbonDioxide) ?? 0f,
            gravityEnabled,
            enabledLights);
    }

    private string PublicId(EntityUid? uid, string prefix)
    {
        return uid is { } entity && Exists(entity)
            ? $"{prefix}-{GetNetEntity(entity).Id}"
            : string.Empty;
    }

    private static int SecondsIntoDay(NiiInstituteComponent institute)
    {
        if (institute.DayDurationSeconds <= 0f)
            return 0;

        return (int) (Math.Clamp(institute.ElapsedSeconds / institute.DayDurationSeconds, 0f, 1f) * 86_399f);
    }

    private static JsonSerializerOptions CreateSerializerOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
