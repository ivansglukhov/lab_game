using Content.Shared.Nii;

namespace Content.Server.Nii.AI;

/// <summary>
/// Compact, JSON-ready observation supplied to the institute narrator.
/// It intentionally contains no EntityUid values or mutation callbacks.
/// </summary>
public sealed record NiiInstituteAiContext(
    int SchemaVersion,
    string Authority,
    int Day,
    int SecondsIntoDay,
    int Balance,
    int DailyFunding,
    int DailyExpenses,
    int Reputation,
    int Science,
    bool IsBankrupt,
    string ProjectId,
    NiiResearchStatus ResearchStatus,
    NiiAiLaboratoryContext[] Laboratories,
    NiiInstituteEventState[] RecentEvents);

public sealed record NiiAiLaboratoryContext(
    string LaboratoryId,
    NiiLaboratoryAssignmentMode AssignmentMode,
    NiiAiEmployeeContext[] Employees,
    NiiAiWorkOrderContext? ActiveWorkOrder,
    NiiAiMachineContext? ResearchMachine,
    NiiAiSensorContext Sensors);

public sealed record NiiAiEmployeeContext(
    string Id,
    string Name,
    NiiEmployeeRole Role,
    NiiEmployeeAvailability Availability,
    string ActiveWorkOrderId);

public sealed record NiiAiWorkOrderContext(
    string Id,
    NiiWorkOrderStatus Status,
    NiiWorkOrderBlockReason BlockReason,
    string AssignedEmployeeId,
    string SampleId,
    float RequiredReagentAvailable);

public sealed record NiiAiMachineContext(
    string Id,
    bool IsProcessing,
    float Progress);

public sealed record NiiAiSensorContext(
    bool AtmosphereAvailable,
    float PressureKpa,
    float TemperatureKelvin,
    float OxygenMoles,
    float PlasmaMoles,
    float CarbonDioxideMoles,
    bool GravityEnabled,
    int EnabledLights);
