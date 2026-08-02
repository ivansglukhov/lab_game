using Content.Server.GameTicking.Rules;
using Content.Shared.GameTicking.Components;

namespace Content.Server.Nii.GameRules;

/// <summary>
/// Creates and cleans up the authoritative institute state with the game rule.
/// </summary>
public sealed partial class NiiPrototypeRuleSystem : GameRuleSystem<NiiPrototypeRuleComponent>
{
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

        component.Institute = null;
    }
}
