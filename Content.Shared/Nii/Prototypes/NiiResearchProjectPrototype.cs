using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared.Nii.Prototypes;

/// <summary>
/// Data-only definition of a research project that can be authorized and completed by the institute.
/// </summary>
[Prototype("niiResearchProject")]
public sealed partial class NiiResearchProjectPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name { get; private set; }

    [DataField]
    public int Cost { get; private set; }

    [DataField]
    public float DurationSeconds { get; private set; } = 20f;

    [DataField]
    public ProtoId<ReagentPrototype> RequiredReagent { get; private set; } = "Plasma";

    [DataField]
    public FixedPoint2 RequiredReagentAmount { get; private set; } = 15;

    [DataField(required: true)]
    public ProtoId<NiiChemicalProcessPrototype> ProductionProcess { get; private set; }

    [DataField(required: true)]
    public EntProtoId ResultPrototype { get; private set; }

    [DataField]
    public int ScienceReward { get; private set; }

    [DataField]
    public int ReputationReward { get; private set; }
}
