using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Robust.Shared.Prototypes;

namespace Content.Shared.Nii.Prototypes;

/// <summary>
/// Data-only, ordered chemical production process executed by institute technicians.
/// Intermediate products remain in the reactor for the following stage.
/// </summary>
[Prototype("niiChemicalProcess")]
public sealed partial class NiiChemicalProcessPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name { get; private set; }

    [DataField(required: true)]
    public List<NiiChemicalProcessStage> Stages { get; private set; } = [];

    [DataField(required: true)]
    public ProtoId<EntityPrototype> ProductEntity { get; private set; }
}

[DataDefinition]
public sealed partial class NiiChemicalProcessStage
{
    [DataField(required: true)]
    public Dictionary<ProtoId<ReagentPrototype>, FixedPoint2> Inputs { get; private set; } = [];

    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Output { get; private set; }

    [DataField(required: true)]
    public FixedPoint2 OutputAmount { get; private set; }

    [DataField]
    public float DurationSeconds { get; private set; } = 5f;
}
