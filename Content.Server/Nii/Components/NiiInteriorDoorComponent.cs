namespace Content.Server.Nii.Components;

/// <summary>
/// Marks an unpowered institute door that opens for nearby actors and active employee routes.
/// </summary>
[RegisterComponent]
public sealed partial class NiiInteriorDoorComponent : Component
{
    [DataField]
    public float ActivationRange = 2.5f;

    [DataField]
    public float CloseDelaySeconds = 2f;

    public float RemainingCloseDelay;

    public bool WasOpen;
}
