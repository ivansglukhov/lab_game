namespace Content.Server.Nii.Components;

/// <summary>
/// A physical machine that consumes the authorized project's reagent and performs the timed work.
/// </summary>
[RegisterComponent]
public sealed partial class NiiResearchMachineComponent : Component
{
    [DataField]
    public string LaboratoryId = "main";

    [DataField]
    public string SolutionName = "beaker";

    public bool IsProcessing;
    public float ElapsedSeconds;
    public EntityUid? Institute;
}

/// <summary>
/// Marks the physical result produced by the first research cycle.
/// </summary>
[RegisterComponent]
public sealed partial class NiiResearchResultComponent : Component;

/// <summary>
/// Marks the prepared reagent container supplied with the prototype map.
/// </summary>
[RegisterComponent]
public sealed partial class NiiResearchSampleComponent : Component;
