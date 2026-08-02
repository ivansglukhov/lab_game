using Robust.Shared.Serialization;

namespace Content.Shared.Nii;

[Serializable, NetSerializable]
public enum NiiEmployeeRole : byte
{
    LaboratoryHead,
    Researcher,
}

[Serializable, NetSerializable]
public enum NiiWorkOrderStatus : byte
{
    Created,
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
}
