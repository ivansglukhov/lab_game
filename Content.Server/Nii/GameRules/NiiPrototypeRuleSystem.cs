using System.Numerics;
using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking;
using Content.Shared.GameTicking.Components;

namespace Content.Server.Nii.GameRules;

/// <summary>
/// Creates and cleans up the authoritative institute state with the game rule.
/// </summary>
public sealed partial class NiiPrototypeRuleSystem : GameRuleSystem<NiiPrototypeRuleComponent>
{
    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    protected override void Started(
        EntityUid uid,
        NiiPrototypeRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.Institute is { } current && Exists(current))
            return;

        component.Institute = Spawn(component.InstitutePrototype);
    }

    protected override void Ended(
        EntityUid uid,
        NiiPrototypeRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleEndedEvent args)
    {
        base.Ended(uid, component, gameRule, args);

        if (component.Institute is { } institute && Exists(institute))
            QueueDel(institute);

        if (component.DirectorTerminal is { } terminal && Exists(terminal))
            QueueDel(terminal);

        component.Institute = null;
        component.DirectorTerminal = null;
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId != "NiiDirector")
            return;

        var query = EntityQueryEnumerator<NiiPrototypeRuleComponent, GameRuleComponent>();
        while (query.MoveNext(out var uid, out var rule, out var gameRule))
        {
            if (!GameTicker.IsGameRuleActive(uid, gameRule))
                continue;

            if (rule.DirectorTerminal is { } current && Exists(current))
                return;

            var coordinates = Transform(args.Mob).Coordinates.Offset(new Vector2(2f, 0f));
            rule.DirectorTerminal = Spawn(rule.DirectorTerminalPrototype, coordinates);
            return;
        }
    }
}
