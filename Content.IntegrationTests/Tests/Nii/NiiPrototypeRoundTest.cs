#nullable enable
using System.Collections.Generic;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Nii.Components;
using Content.Server.Nii.Systems;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Nii;
using Content.Shared.Preferences;
using Content.Shared.Nii.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Wall;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Server.GameObjects;

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

        Assert.That(playerTransform.GridUid, Is.Not.Null);
        var gravity = SEntMan.GetComponent<GravityComponent>(playerTransform.GridUid!.Value);
        Assert.That(gravity.Enabled, Is.True);
        Assert.That(gravity.Inherent, Is.True);

        var enabledLightsOnGrid = 0;
        var lights = SEntMan.EntityQueryEnumerator<PointLightComponent, TransformComponent>();
        while (lights.MoveNext(out _, out var light, out var lightTransform))
        {
            if (light.Enabled && lightTransform.GridUid == playerTransform.GridUid)
                enabledLightsOnGrid++;
        }
        Assert.That(enabledLightsOnGrid, Is.GreaterThanOrEqualTo(8));

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

        var laboratories = SEntMan.EntityQueryEnumerator<NiiLaboratoryComponent>();
        Assert.That(laboratories.MoveNext(out var laboratoryUid, out var laboratory), Is.True);
        Assert.That(laboratory!.Institute, Is.EqualTo(instituteUid));
        Assert.That(laboratory.Head, Is.Not.Null);
        Assert.That(laboratory.Researchers, Has.Count.EqualTo(1));
        Assert.That(laboratory.ResearchMachine, Is.Not.Null);
        Assert.That(laboratories.MoveNext(out _, out _), Is.False);
        Assert.That(institute.Laboratories, Is.EqualTo(new[] { laboratoryUid }));

        var employees = SEntMan.EntityQueryEnumerator<NiiEmployeeComponent>();
        var employeeRoles = new List<NiiEmployeeRole>();
        while (employees.MoveNext(out var employeeUid, out var employee))
        {
            Assert.That(employee.Laboratory, Is.EqualTo(laboratoryUid));
            Assert.That(
                SEntMan.GetComponent<HumanoidProfileComponent>(employeeUid).Species,
                Is.EqualTo(new ProtoId<SpeciesPrototype>("Human")));
            employeeRoles.Add(employee.Role);
        }
        Assert.That(employeeRoles, Is.EquivalentTo(new[]
        {
            NiiEmployeeRole.LaboratoryHead,
            NiiEmployeeRole.Researcher,
        }));

        var project = Server.ProtoMan.Index(institute.ActiveProject);
        var terminalSystem = Server.System<NiiDirectorTerminalSystem>();
        Assert.That(terminalSystem.TryAuthorizeResearch(institute), Is.True);
        Assert.That(institute.ResearchStatus, Is.EqualTo(NiiResearchStatus.Authorized));
        Assert.That(institute.Balance, Is.EqualTo(500_000 - project.Cost));
        Assert.That(terminalSystem.TryAuthorizeResearch(institute), Is.False);
        Assert.That(institute.Balance, Is.EqualTo(500_000 - project.Cost));

        var machines = SEntMan.EntityQueryEnumerator<NiiResearchMachineComponent>();
        Assert.That(machines.MoveNext(out var machineUid, out var machine), Is.True);
        Assert.That(machines.MoveNext(out _, out _), Is.False);

        var samples = SEntMan.EntityQueryEnumerator<NiiResearchSampleComponent>();
        Assert.That(samples.MoveNext(out var sampleUid, out _), Is.True);
        Assert.That(samples.MoveNext(out _, out _), Is.False);

        var researchSystem = Server.System<NiiResearchMachineSystem>();
        var machineEntity = new Entity<NiiResearchMachineComponent>(machineUid, machine!);
        Assert.That(researchSystem.TryStartResearch(machineEntity, sampleUid, player.Value), Is.True);
        Assert.That(institute.ResearchStatus, Is.EqualTo(NiiResearchStatus.Running));

        var solutions = Server.System<SharedSolutionContainerSystem>();
        Assert.That(solutions.TryGetSolution(sampleUid, "beaker", out _, out var sampleSolution), Is.True);
        Assert.That(sampleSolution!.GetTotalPrototypeQuantity(project.RequiredReagent), Is.EqualTo(FixedPoint2.Zero));

        machine!.ElapsedSeconds = project.DurationSeconds;
        await Pair.RunTicksSync(2);
        Assert.That(institute.ResearchStatus, Is.EqualTo(NiiResearchStatus.Completed));
        Assert.That(institute.Science, Is.EqualTo(project.ScienceReward));
        Assert.That(institute.Reputation, Is.EqualTo(project.ReputationReward));

        var results = SEntMan.EntityQueryEnumerator<NiiResearchResultComponent>();
        Assert.That(results.MoveNext(out var resultUid, out _), Is.True);
        Assert.That(resultUid.IsValid(), Is.True);
        Assert.That(results.MoveNext(out _, out _), Is.False);

        var balanceBeforeDays = institute.Balance;
        institute.ElapsedSeconds = 0f;
        institute.DayDurationSeconds = 0.01f;
        await Pair.RunTicksSync(2);
        Assert.That(institute.CurrentDay, Is.GreaterThan(1));
        Assert.That(institute.Balance, Is.EqualTo(balanceBeforeDays - 25_000 * (institute.CurrentDay - 1)));

        await Server.WaitPost(() => ticker.RestartRound());
    }
}
