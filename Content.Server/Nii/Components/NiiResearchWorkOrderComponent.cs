using Content.Shared.Nii;
using Content.Shared.Nii.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server.Nii.Components;

/// <summary>
/// A durable management-level instruction that can later be executed by an NPC.
/// </summary>
[RegisterComponent]
public sealed partial class NiiResearchWorkOrderComponent : Component
{
    [DataField]
    public ProtoId<NiiResearchProjectPrototype> Project = "NiiStablePlasmaCulture";

    [DataField]
    public NiiWorkOrderStatus Status = NiiWorkOrderStatus.Created;

    [DataField]
    public NiiWorkOrderBlockReason BlockReason = NiiWorkOrderBlockReason.None;

    public EntityUid? Institute;
    public EntityUid? Laboratory;
    public EntityUid? RequestedBy;
    public EntityUid? AssignedTo;
    public EntityUid? Sample;
    public EntityUid? Machine;
}
