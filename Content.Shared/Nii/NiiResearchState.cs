using Robust.Shared.Serialization;

namespace Content.Shared.Nii;

[Serializable, NetSerializable]
public enum NiiResearchStatus : byte
{
    Available,
    Authorized,
    Running,
    Completed,
}

[Serializable, NetSerializable]
public enum NiiInstituteEventType : byte
{
    InstituteStarted,
    DayAdvanced,
    FinancialWarning,
    ProjectAuthorized,
    WorkOrderCreated,
    ProductionOrderCreated,
    TechnicianAssigned,
    ProductionInputReserved,
    ProductionInputDelivered,
    ProductionStageStarted,
    ProductionStageCompleted,
    ReagentProduced,
    ProductionBlocked,
    AssignmentModeChanged,
    EmployeeAssigned,
    SampleReserved,
    SampleDeliveryStarted,
    ResearchStarted,
    ResearchCompleted,
    ResourceShortage,
    WorkOrderBlocked,
    WorkOrderRecovered,
    EmployeeUnavailable,
}

[Serializable, NetSerializable]
public enum NiiInstituteEventSeverity : byte
{
    Info,
    Attention,
    Critical,
    Success,
}

[Serializable, NetSerializable]
public enum NiiAiMessageKind : byte
{
    Report,
    Alert,
    Success,
}
