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
            EntityUid? routedEmployee = null;
            var actors = EntityQueryEnumerator<MobStateComponent, TransformComponent>();
            while (actors.MoveNext(out var actorUid, out _, out var actorTransform))
            {
                var isEmployee = HasComp<NiiEmployeeComponent>(actorUid);
                if (isEmployee &&
                    TryComp<NPCSteeringComponent>(actorUid, out var steering) &&
                    RouteUsesDoor(steering, doorTransform))
                    routedEmployee ??= actorUid;

                // Idle staff work close to the laboratory doorway, so only their actual route should hold it open.
                if (!isEmployee &&
                    doorTransform.Coordinates.TryDistance(EntityManager, actorTransform.Coordinates, out var distance) &&
                    distance <= automaticDoor.ActivationRange)
                    nearbyActor ??= actorUid;
            }

            if (nearbyActor is not null || routedEmployee is not null)
            {
                automaticDoor.RemainingCloseDelay = automaticDoor.CloseDelaySeconds;
                if (door.State is DoorState.Closed or DoorState.Closing)
                    _doors.TryOpen(doorUid, door);

                if (door.State == DoorState.Open && !automaticDoor.WasOpen)
                {
                    // Rebuild only after the opening animation has disabled collision.
                    _steering.Unregister(routedEmployee ?? nearbyActor!.Value);
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

    private static bool RouteUsesDoor(NPCSteeringComponent steering, TransformComponent doorTransform)
    {
        if (doorTransform.GridUid is not { } doorGrid)
            return false;

        foreach (var node in steering.CurrentPath)
        {
            if (node.GraphUid == doorGrid && node.Box.Contains(doorTransform.LocalPosition))
                return true;
        }

        return false;
    }
}
