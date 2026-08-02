using Content.Shared.Nii;
using Content.Shared.Nii.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Nii.Components;

/// <summary>
/// Stores the authoritative simulation state for an NII prototype round.
/// </summary>
[RegisterComponent]
public sealed partial class NiiInstituteComponent : Component
{
    [DataField("balance")]
    public int Balance = 500_000;

    [DataField("dailyFunding")]
    public int DailyFunding = 125_000;

    [DataField("dailyExpenses")]
    public int DailyExpenses = 150_000;

    [DataField("reputation")]
    public int Reputation;

    [DataField("science")]
    public int Science;

    [DataField("activeProject")]
    public ProtoId<NiiResearchProjectPrototype> ActiveProject = "NiiStablePlasmaCulture";

    [DataField("researchStatus")]
    public NiiResearchStatus ResearchStatus = NiiResearchStatus.Available;

    [DataField("eventLog")]
    public List<NiiInstituteEventType> EventLog = [NiiInstituteEventType.InstituteStarted];

    [DataField("currentDay")]
    public int CurrentDay = 1;

    [DataField("dayDurationSeconds")]
    public float DayDurationSeconds = 120f;

    public List<EntityUid> Laboratories = [];

    public EntityUid? ActiveWorkOrder;

    public EntityUid? LastWorkOrder;

    /// <summary>
    /// Negative balance is a soft failure state and does not stop the round.
    /// </summary>
    public bool IsBankrupt;

    /// <summary>
    /// Runtime-only time accumulated toward the next institute day.
    /// </summary>
    public float ElapsedSeconds;
}
