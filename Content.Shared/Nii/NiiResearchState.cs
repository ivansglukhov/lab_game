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
    ProjectAuthorized,
    ResearchStarted,
    ResearchCompleted,
}
