using Content.Shared.Atmos;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Nii;
using Content.Shared.Nii.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Nii.Components;

[RegisterComponent]
public sealed partial class NiiChemicalReactorComponent : Component
{
    [DataField]
    public string LaboratoryId = "main";

    [DataField]
    public string SolutionName = "reactor";

    public EntityUid? ActiveOrder;
    public bool IsProcessing;
    public float ElapsedSeconds;
    public GasMixture GasBuffer = new(5f) { Temperature = Atmospherics.T20C };
}

[RegisterComponent]
public sealed partial class NiiChemicalStockComponent : Component
{
    [DataField(required: true)]
    public ProtoId<ReagentPrototype> Reagent;

    [DataField]
    public string SolutionName = "beaker";

    public EntityUid? ReservedBy;
}

[RegisterComponent]
public sealed partial class NiiGasStockComponent : Component
{
    [DataField(required: true)]
    public Gas Gas;

    [DataField]
    public EntProtoId PayloadEntity = "NiiOxygenTransferTank";

    public EntityUid? ReservedBy;
}

[RegisterComponent]
public sealed partial class NiiProductionOrderComponent : Component
{
    [DataField]
    public ProtoId<NiiChemicalProcessPrototype> Process = "NiiCopperSulfateProcess";

    public NiiProductionOrderStatus Status = NiiProductionOrderStatus.Created;
    public EntityUid? Institute;
    public EntityUid? Laboratory;
    public EntityUid? ResearchOrder;
    public EntityUid? AssignedTo;
    public EntityUid? Reactor;
    public EntityUid? CurrentSource;
    public EntityUid? CurrentPayload;
    public ProtoId<ReagentPrototype>? CurrentInput;
    public FixedPoint2 CurrentInputAmount;
    public Gas? CurrentGas;
    public float CurrentGasAmount;
    public int StageIndex;
}
