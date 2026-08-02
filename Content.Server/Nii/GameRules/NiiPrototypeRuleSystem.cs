using Content.Server.GameTicking.Rules;
using Content.Server.Nii.Components;
using Content.Server.Nii.Systems;
using Content.Shared.GameTicking.Components;
using Content.Shared.Nii;

namespace Content.Server.Nii.GameRules;

/// <summary>
/// Creates and cleans up the authoritative institute state with the game rule.
/// </summary>
public sealed partial class NiiPrototypeRuleSystem : GameRuleSystem<NiiPrototypeRuleComponent>
{
    [Dependency] private NiiLaboratorySystem _laboratories = default!;
    [Dependency] private NiiInstituteNarrativeSystem _narrative = default!;

    protected override void Started(
        EntityUid uid,
        NiiPrototypeRuleComponent component,
        GameRuleComponent gameRule,
        GameRuleStartedEvent args)
    {
        base.Started(uid, component, gameRule, args);

        if (component.Institute is { } current && Exists(current))
            return;

        var instituteUid = Spawn(component.InstitutePrototype);
        component.Institute = instituteUid;
        _laboratories.ReconcileNow();
        _narrative.Record(
            (instituteUid, Comp<NiiInstituteComponent>(instituteUid)),
            NiiInstituteEventType.InstituteStarted);
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
