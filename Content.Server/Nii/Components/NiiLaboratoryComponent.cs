using Content.Shared.Nii;

namespace Content.Server.Nii.Components;

/// <summary>
/// Stores the authoritative state of one physical institute laboratory.
/// </summary>
[RegisterComponent]
public sealed partial class NiiLaboratoryComponent : Component
{
    [DataField]
    public string LaboratoryId = "main";

    public EntityUid? Institute;
    public EntityUid? Head;
    public List<EntityUid> Researchers = [];
    public EntityUid? ResearchMachine;
    public EntityUid? ActiveWorkOrder;
}

/// <summary>
/// Marks a human institute employee and their place in the laboratory hierarchy.
/// </summary>
[RegisterComponent]
public sealed partial class NiiEmployeeComponent : Component
{
    [DataField]
    public string LaboratoryId = "main";

    [DataField]
    public NiiEmployeeRole Role = NiiEmployeeRole.Researcher;

    public EntityUid? Laboratory;
    public EntityUid? ActiveWorkOrder;
}
