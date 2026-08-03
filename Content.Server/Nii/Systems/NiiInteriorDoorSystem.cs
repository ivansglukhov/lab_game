using Content.Server.Nii.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Mobs.Components;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Provides reliable automatic doors for the prototype map without requiring an APC network.
/// </summary>
public sealed partial class NiiInteriorDoorSystem : EntitySystem
{
    [Dependency] private SharedDoorSystem _doors = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NiiInteriorDoorComponent, DoorComponent, TransformComponent>();
        while (query.MoveNext(out var doorUid, out var automaticDoor, out var door, out var doorTransform))
        {
            EntityUid? nearbyActor = null;
            EntityUid? navigatingEmployee = null;
            var actors = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
            while (actors.MoveNext(out var actorUid, out _, out var actorTransform))
            {
                if (HasComp<NiiEmployeeComponent>(actorUid) && HasComp<NPCSteeringComponent>(actorUid))
                    navigatingEmployee ??= actorUid;

                if (doorTransform.Coordinates.TryDistance(EntityManager, actorTransform.Coordinates, out var distance) &&
                    distance <= automaticDoor.ActivationRange)
                    nearbyActor ??= actorUid;
            }

            if (nearbyActor is not null || navigatingEmployee is not null)
            {
                automaticDoor.RemainingCloseDelay = automaticDoor.CloseDelaySeconds;
                if (door.State is DoorState.Closed or DoorState.Closing)
                    _doors.TryOpen(doorUid, door);

                if (door.State == DoorState.Open && !automaticDoor.WasOpen)
                {
                    // Rebuild only after the opening animation has disabled collision.
                    _steering.Unregister(navigatingEmployee ?? nearbyActor!.Value);
                }

                automaticDoor.WasOpen = door.State == DoorState.Open;
                continue;
            }

            if (door.State != DoorState.Open)
            {
                automaticDoor.WasOpen = false;
                continue;
            }

            automaticDoor.WasOpen = true;

            automaticDoor.RemainingCloseDelay -= frameTime;
            if (automaticDoor.RemainingCloseDelay <= 0f)
                _doors.TryClose(doorUid, door);
        }
    }
}
