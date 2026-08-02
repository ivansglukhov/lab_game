using Robust.Shared.Prototypes;

namespace Content.Server.Nii.GameRules;

/// <summary>
/// Owns the institute state entity for the lifetime of an NII prototype round.
/// </summary>
[RegisterComponent]
public sealed partial class NiiPrototypeRuleComponent : Component
{
    [DataField("institutePrototype")]
    public EntProtoId InstitutePrototype = "NiiInstituteState";

    public EntityUid? Institute;
}
