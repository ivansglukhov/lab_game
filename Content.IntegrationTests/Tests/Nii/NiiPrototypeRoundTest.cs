#nullable enable
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Nii.Components;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Nii.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Wall;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Nii;

[TestFixture]
public sealed class NiiPrototypeRoundTest : GameTest
{
    private static readonly ProtoId<JobPrototype> Director = "NiiDirector";

    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true,
    };

    [Test]
    public async Task StartsDirectorAndInstituteState()
    {
        Server.CfgMan.SetCVar(CCVars.GameMap, "NiiPrototypeMap");
        var ticker = Server.System<GameTicker>();
        ticker.SetGamePreset("NiiPrototype");

        await Pair.SetJobPriorities((Director, JobPriority.High));
        ticker.ToggleReadyAll(true);
        await Server.WaitPost(() => ticker.StartRound());
        await Pair.RunTicksSync(10);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));
        Assert.That(ticker.CurrentPreset?.ID, Is.EqualTo("NiiPrototype"));

        var player = Server.PlayerMan.SessionsDict[Client.User!.Value].AttachedEntity;
        Assert.That(player, Is.Not.Null);

        var mindSystem = Server.System<MindSystem>();
        var jobSystem = Server.System<SharedJobSystem>();
        var mind = mindSystem.GetMind(player!.Value);
        Assert.That(jobSystem.MindTryGetJobId(mind, out var job));
        Assert.That(job, Is.EqualTo(Director));

        var terminals = SEntMan.EntityQueryEnumerator<NiiDirectorTerminalComponent>();
        Assert.That(terminals.MoveNext(out var terminalUid, out _), Is.True);
        Assert.That(terminalUid.IsValid(), Is.True);
        Assert.That(terminals.MoveNext(out _, out _), Is.False);

        var playerTransform = SEntMan.GetComponent<TransformComponent>(player.Value);
        var terminalTransform = SEntMan.GetComponent<TransformComponent>(terminalUid);
        Assert.That(terminalTransform.GridUid, Is.EqualTo(playerTransform.GridUid));
        Assert.That(terminalTransform.LocalPosition, Is.EqualTo(new Vector2(2.5f, 12.5f)));

        var wallCount = 0;
        var walls = SEntMan.EntityQueryEnumerator<WallComponent>();
        while (walls.MoveNext(out _))
        {
            wallCount++;
        }
        Assert.That(wallCount, Is.EqualTo(94));

        var atmosphere = Server.System<AtmosphereSystem>().GetContainingMixture(player.Value);
        Assert.That(atmosphere, Is.Not.Null);
        Assert.That(atmosphere!.GetMoles(Gas.Oxygen), Is.GreaterThan(0));

        var institutes = SEntMan.EntityQueryEnumerator<NiiInstituteComponent>();
        Assert.That(institutes.MoveNext(out var instituteUid, out var institute), Is.True);
        Assert.That(instituteUid.IsValid(), Is.True);
        Assert.That(institute!.Balance, Is.EqualTo(500_000));
        Assert.That(institutes.MoveNext(out _, out _), Is.False);

        institute.ElapsedSeconds = 0f;
        institute.DayDurationSeconds = 0.01f;
        await Pair.RunTicksSync(2);
        Assert.That(institute.CurrentDay, Is.GreaterThan(1));
        Assert.That(institute.Balance, Is.EqualTo(500_000 - 25_000 * (institute.CurrentDay - 1)));

        await Server.WaitPost(() => ticker.RestartRound());
    }
}
