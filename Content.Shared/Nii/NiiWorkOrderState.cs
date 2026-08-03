using Robust.Shared.Serialization;

namespace Content.Shared.Nii;

[Serializable, NetSerializable]
public enum NiiEmployeeRole : byte
{
    LaboratoryHead,
    Researcher,
    LaboratoryTechnician,
}

[Serializable, NetSerializable]
public enum NiiEmployeeAvailability : byte
{
    Available,
    Busy,
    Unavailable,
}

[Serializable, NetSerializable]
public enum NiiLaboratoryAssignmentMode : byte
{
    Manual,
    Delegated,
}

[Serializable, NetSerializable]
public enum NiiWorkOrderStatus : byte
{
    Created,
    AwaitingProduction,
    AwaitingAssignment,
    Assigned,
    FetchingSample,
    DeliveringSample,
    Running,
    Completed,
    Blocked,
    Cancelled,
}

[Serializable, NetSerializable]
public enum NiiWorkOrderBlockReason : byte
{
    None,
    NoSample,
    SampleInaccessible,
    MachineBusy,
    MachineInaccessible,
    EmployeeUnavailable,
    ProductionUnavailable,
}

[Serializable, NetSerializable]
public enum NiiProductionOrderStatus : byte
{
    Created,
    FetchingInputs,
    DeliveringInput,
    Processing,
    Completed,
    Blocked,
    Cancelled,
}
