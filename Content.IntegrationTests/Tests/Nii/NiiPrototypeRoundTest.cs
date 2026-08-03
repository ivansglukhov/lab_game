#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.UserInterface.Systems.Chat;
using Content.IntegrationTests.Fixtures;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Mind;
using Content.Server.Nii.AI;
using Content.Server.Nii.Components;
using Content.Server.Nii.Systems;
using Content.Server.NPC.HTN;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Nii;
using Content.Shared.NPC;
using Content.Shared.Preferences;
using Content.Shared.Nii.Components;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Content.Shared.Wall;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Server.GameObjects;
using Robust.Client.UserInterface;

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
        Assert.That(institute.EventLog, Has.Count.EqualTo(1));
        Assert.That(institute.EventLog[0].Type, Is.EqualTo(NiiInstituteEventType.InstituteStarted));
        Assert.That(institute.EventLog[0].SchemaVersion, Is.EqualTo(NiiInstituteNarrativeSystem.SchemaVersion));
        Assert.That(institute.AiMessages, Has.Count.EqualTo(1));
        Assert.That(institute.AiMessages[0].RelatedEventSequence, Is.EqualTo(institute.EventLog[0].Sequence));

        var instituteChat = Server.System<NiiInstituteChatSystem>();
        var directorRecipients = instituteChat.GetDirectorRecipients();
        Assert.That(directorRecipients, Has.Count.EqualTo(1));
        Assert.That(directorRecipients[0].UserId, Is.EqualTo(Client.User));

        await Pair.RunTicksSync(2);
        var initialNiiChatMessages = Array.Empty<string>();
        await Client.WaitPost(() =>
        {
            var chat = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
            initialNiiChatMessages = chat.History.Select(entry => entry.Msg.Message).ToArray();
        });
        Assert.That(initialNiiChatMessages.Count(message => message.StartsWith("[НИИ ·")), Is.EqualTo(1));
        Assert.That(initialNiiChatMessages.Count(message => message.StartsWith("[ИИ института]")), Is.EqualTo(1));

        await Server.WaitPost(() => instituteChat.SendDirectorCommand(player.Value, "Проверка команды."));
        await Pair.RunTicksSync(2);
        var directorCommandMessages = Array.Empty<string>();
        await Client.WaitPost(() =>
        {
            var chat = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<ChatUIController>();
            directorCommandMessages = chat.History
                .Select(entry => entry.Msg.Message)
                .Where(message => message == "[Команда директора] Проверка команды.")
                .ToArray();
        });
        Assert.That(directorCommandMessages, Has.Length.EqualTo(1));

        var laboratories = SEntMan.EntityQueryEnumerator<NiiLaboratoryComponent>();
        Assert.That(laboratories.MoveNext(out var laboratoryUid, out var laboratory), Is.True);
        Assert.That(laboratory!.Institute, Is.EqualTo(instituteUid));
        Assert.That(laboratory.Head, Is.Not.Null);
        Assert.That(laboratory.Researchers, Has.Count.EqualTo(2));
        Assert.That(laboratory.Technicians, Has.Count.EqualTo(2));
        Assert.That(laboratory.ResearchMachine, Is.Not.Null);
        Assert.That(laboratory.ChemicalReactor, Is.Not.Null);
        Assert.That(laboratories.MoveNext(out _, out _), Is.False);
        Assert.That(institute.Laboratories, Is.EqualTo(new[] { laboratoryUid }));

        var contextSystem = Server.System<NiiInstituteAiContextSystem>();
        var initialContext = contextSystem.Build((instituteUid, institute));
        Assert.That(initialContext.Authority, Is.EqualTo(NiiInstituteAiContextSystem.Authority));
        Assert.That(initialContext.Laboratories, Has.Length.EqualTo(1));
        Assert.That(initialContext.Laboratories[0].Employees, Has.Length.EqualTo(5));
        Assert.That(initialContext.Laboratories[0].Sensors.AtmosphereAvailable, Is.True);
        Assert.That(initialContext.Laboratories[0].Sensors.PressureKpa, Is.GreaterThan(0f));
        Assert.That(initialContext.Laboratories[0].Sensors.OxygenMoles, Is.GreaterThan(0f));
        Assert.That(initialContext.Laboratories[0].Sensors.GravityEnabled, Is.True);
        Assert.That(initialContext.Laboratories[0].Sensors.EnabledLights, Is.GreaterThanOrEqualTo(8));
        var initialContextJson = contextSystem.Serialize(initialContext);
        Assert.That(initialContextJson, Does.Not.Contain("EntityUid"));
        Assert.That(initialContextJson, Does.Contain("\"authority\":\"observe_only\""));
        Assert.That(initialContextJson, Does.Contain("\"researchStatus\":\"available\""));
        Assert.That(initialContextJson.Length, Is.LessThan(16_384));

        var employees = SEntMan.EntityQueryEnumerator<NiiEmployeeComponent>();
        var employeeRoles = new List<NiiEmployeeRole>();
        var researcherUids = new List<EntityUid>();
        var technicianUids = new List<EntityUid>();
        while (employees.MoveNext(out var employeeUid, out var employee))
        {
            Assert.That(employee.Laboratory, Is.EqualTo(laboratoryUid));
            Assert.That(
                SEntMan.GetComponent<HumanoidProfileComponent>(employeeUid).Species,
                Is.EqualTo(new ProtoId<SpeciesPrototype>("Human")));
            employeeRoles.Add(employee.Role);
            if (employee.Role == NiiEmployeeRole.Researcher)
                researcherUids.Add(employeeUid);
            if (employee.Role == NiiEmployeeRole.LaboratoryTechnician)
                technicianUids.Add(employeeUid);
        }
        Assert.That(employeeRoles, Is.EquivalentTo(new[]
        {
            NiiEmployeeRole.LaboratoryHead,
            NiiEmployeeRole.Researcher,
            NiiEmployeeRole.Researcher,
            NiiEmployeeRole.LaboratoryTechnician,
            NiiEmployeeRole.LaboratoryTechnician,
        }));
        Assert.That(researcherUids, Has.Count.EqualTo(2));
        Assert.That(technicianUids, Has.Count.EqualTo(2));
        var researcherUid = researcherUids[0];
        Assert.That(researcherUid.IsValid(), Is.True);
        Assert.That(SEntMan.HasComponent<HTNComponent>(researcherUid), Is.True);
        Assert.That(SEntMan.HasComponent<ActiveNPCComponent>(researcherUid), Is.True);
        var researcherStartPosition = SEntMan.GetComponent<TransformComponent>(researcherUid).LocalPosition;
        var technicianUid = technicianUids[0];
        var technicianStartPosition = SEntMan.GetComponent<TransformComponent>(technicianUid).LocalPosition;

        var solutions = Server.System<SharedSolutionContainerSystem>();
        FixedPoint2 StockAmount(ProtoId<ReagentPrototype> reagent)
        {
            var stocks = SEntMan.EntityQueryEnumerator<NiiChemicalStockComponent>();
            while (stocks.MoveNext(out var stockUid, out var stock))
            {
                if (stock.Reagent != reagent ||
                    !solutions.TryGetSolution(stockUid, stock.SolutionName, out _, out var solution))
                    continue;

                return solution!.GetTotalPrototypeQuantity(reagent);
            }

            Assert.Fail($"Stock for {reagent} was not found.");
            return FixedPoint2.Zero;
        }

        Assert.That(StockAmount("Copper"), Is.EqualTo(FixedPoint2.New(30)));
        Assert.That(StockAmount("Oxygen"), Is.EqualTo(FixedPoint2.New(30)));
        Assert.That(StockAmount("SulfuricAcid"), Is.EqualTo(FixedPoint2.New(60)));

        var initialSamples = SEntMan.EntityQueryEnumerator<NiiResearchSampleComponent>();
        Assert.That(initialSamples.MoveNext(out _, out _), Is.False);

        var project = Server.ProtoMan.Index(institute.ActiveProject);
        var terminalSystem = Server.System<NiiDirectorTerminalSystem>();
        var authorized = false;
        await Server.WaitPost(() => authorized = terminalSystem.TryAuthorizeResearch((instituteUid, institute)));
        Assert.That(authorized, Is.True);
        Assert.That(institute.ResearchStatus, Is.EqualTo(NiiResearchStatus.Authorized));
        Assert.That(institute.Balance, Is.EqualTo(500_000 - project.Cost));
        Assert.That(institute.EventLog.Count(eventState =>
            eventState.Type == NiiInstituteEventType.WorkOrderCreated), Is.EqualTo(1));
        Assert.That(institute.EventLog.Count(eventState =>
            eventState.Type == NiiInstituteEventType.ProjectAuthorized), Is.EqualTo(1));
        var latestAiEvent = institute.EventLog.Single(eventState =>
            eventState.Sequence == institute.AiMessages[^1].RelatedEventSequence);
        Assert.That(latestAiEvent.Type, Is.EqualTo(NiiInstituteEventType.ProductionOrderCreated));
        await Server.WaitPost(() => authorized = terminalSystem.TryAuthorizeResearch((instituteUid, institute)));
        Assert.That(authorized, Is.False);
        Assert.That(institute.Balance, Is.EqualTo(500_000 - project.Cost));

        var workOrders = SEntMan.EntityQueryEnumerator<NiiResearchWorkOrderComponent>();
        Assert.That(workOrders.MoveNext(out var workOrderUid, out var workOrder), Is.True);
        Assert.That(workOrders.MoveNext(out _, out _), Is.False);
        Assert.That(institute.ActiveWorkOrder, Is.EqualTo(workOrderUid));
        Assert.That(laboratory.ActiveWorkOrder, Is.EqualTo(workOrderUid));
        Assert.That(workOrder!.Institute, Is.EqualTo(instituteUid));
        Assert.That(workOrder.Laboratory, Is.EqualTo(laboratoryUid));
        Assert.That(workOrder.AssignedTo, Is.Null);
        Assert.That(workOrder.Machine, Is.Null);
        Assert.That(workOrder.Sample, Is.Null);
        Assert.That(workOrder.Status, Is.EqualTo(NiiWorkOrderStatus.AwaitingProduction));
        Assert.That(workOrder.ProductionOrder, Is.Not.Null);

        var productionOrders = SEntMan.EntityQueryEnumerator<NiiProductionOrderComponent>();
        Assert.That(productionOrders.MoveNext(out var productionOrderUid, out var productionOrder), Is.True);
        Assert.That(productionOrders.MoveNext(out _, out _), Is.False);
        Assert.That(productionOrderUid, Is.EqualTo(workOrder.ProductionOrder));
        Assert.That(productionOrder!.AssignedTo, Is.EqualTo(technicianUid));
        Assert.That(productionOrder.Reactor, Is.EqualTo(laboratory.ChemicalReactor));
        Assert.That(SEntMan.GetComponent<NiiEmployeeComponent>(technicianUid).ActiveWorkOrder,
            Is.EqualTo(productionOrderUid));

        await Pair.RunTicksSync(10);
        Assert.That(SEntMan.GetComponent<TransformComponent>(researcherUid).LocalPosition,
            Is.EqualTo(researcherStartPosition));

        for (var i = 0; i < 300 && workOrder.Status == NiiWorkOrderStatus.AwaitingProduction; i++)
            await Pair.RunTicksSync(5);

        var technicianHtn = SEntMan.GetComponent<HTNComponent>(technicianUid);
        var reactorContents = "unavailable";
        if (laboratory.ChemicalReactor is { } diagnosticReactor &&
            solutions.TryGetSolution(diagnosticReactor, "reactor", out _, out var diagnosticSolution))
        {
            reactorContents = $"Cu={diagnosticSolution!.GetTotalPrototypeQuantity("Copper")}," +
                              $"O2={diagnosticSolution.GetTotalPrototypeQuantity("Oxygen")}," +
                              $"CuO={diagnosticSolution.GetTotalPrototypeQuantity("CopperOxide")}";
        }
        Assert.That(
            workOrder.Status,
            Is.EqualTo(NiiWorkOrderStatus.AwaitingAssignment),
            $"production={productionOrder.Status}, stage={productionOrder.StageIndex}, " +
            $"input={productionOrder.CurrentInput}, reactor={reactorContents}, " +
            $"stocks=Cu:{StockAmount("Copper")}/O2:{StockAmount("Oxygen")}/acid:{StockAmount("SulfuricAcid")}, " +
            $"plan={technicianHtn.Plan?.CurrentOperator.GetType().Name ?? "none"}, " +
            $"position={SEntMan.GetComponent<TransformComponent>(technicianUid).LocalPosition}");
        Assert.That(productionOrder.Status, Is.EqualTo(NiiProductionOrderStatus.Completed));
        Assert.That(laboratory.ActiveProductionOrder, Is.Null);
        Assert.That(SEntMan.GetComponent<NiiEmployeeComponent>(technicianUid).ActiveWorkOrder, Is.Null);
        Assert.That(SEntMan.GetComponent<TransformComponent>(technicianUid).LocalPosition,
            Is.Not.EqualTo(technicianStartPosition));
        Assert.That(StockAmount("Copper"), Is.EqualTo(FixedPoint2.New(25)));
        Assert.That(StockAmount("Oxygen"), Is.EqualTo(FixedPoint2.New(25)));
        Assert.That(StockAmount("SulfuricAcid"), Is.EqualTo(FixedPoint2.New(50)));
        Assert.That(institute.EventLog.Count(eventState =>
            eventState.Type == NiiInstituteEventType.ProductionOrderCreated), Is.EqualTo(1));
        Assert.That(institute.EventLog.Count(eventState =>
            eventState.Type == NiiInstituteEventType.ProductionStageStarted), Is.EqualTo(2));
        Assert.That(institute.EventLog.Count(eventState =>
            eventState.Type == NiiInstituteEventType.ProductionStageCompleted), Is.EqualTo(2));
        Assert.That(institute.EventLog.Count(eventState =>
            eventState.Type == NiiInstituteEventType.ReagentProduced), Is.EqualTo(1));

        var assigned = false;
        await Server.WaitPost(() => assigned = terminalSystem.TryAssignResearcher(
            (instituteUid, institute), laboratory.Head!.Value));
        Assert.That(assigned, Is.False);
        await Server.WaitPost(() => assigned = terminalSystem.TryAssignResearcher(
            (instituteUid, institute), researcherUid));
        Assert.That(assigned, Is.True);
        Assert.That(workOrder.AssignedTo, Is.EqualTo(researcherUid));
        var assignmentEvent = institute.EventLog.Last(eventState =>
            eventState.Type == NiiInstituteEventType.EmployeeAssigned);
        Assert.That(assignmentEvent.ActorName, Is.EqualTo(SEntMan.GetComponent<MetaDataComponent>(researcherUid).EntityName));
        Assert.That(assignmentEvent.ActorId, Is.Not.Empty);
        Assert.That(workOrder.Machine, Is.EqualTo(laboratory.ResearchMachine));
        Assert.That(workOrder.Sample, Is.Not.Null);
        Assert.That(workOrder.Status, Is.EqualTo(NiiWorkOrderStatus.FetchingSample));
        Assert.That(SEntMan.GetComponent<NiiEmployeeComponent>(workOrder.AssignedTo!.Value).ActiveWorkOrder,
            Is.EqualTo(workOrderUid));
        Assert.That(SEntMan.GetComponent<NiiEmployeeComponent>(researcherUids[1]).ActiveWorkOrder, Is.Null);

        var machines = SEntMan.EntityQueryEnumerator<NiiResearchMachineComponent>();
        Assert.That(machines.MoveNext(out var machineUid, out var machine), Is.True);
        Assert.That(machines.MoveNext(out _, out _), Is.False);

        var samples = SEntMan.EntityQueryEnumerator<NiiResearchSampleComponent>();
        Assert.That(samples.MoveNext(out var sampleUid, out _), Is.True);
        Assert.That(samples.MoveNext(out _, out _), Is.False);

        for (var i = 0; i < 120 && institute.ResearchStatus == NiiResearchStatus.Authorized; i++)
            await Pair.RunTicksSync(5);

        var researcherHtn = SEntMan.GetComponent<HTNComponent>(researcherUid);
        Assert.That(
            institute.ResearchStatus,
            Is.EqualTo(NiiResearchStatus.Running),
            $"order={workOrder.Status}, block={workOrder.BlockReason}, " +
            $"plan={researcherHtn.Plan?.CurrentOperator.GetType().Name ?? "none"}, " +
            $"position={SEntMan.GetComponent<TransformComponent>(researcherUid).LocalPosition}");
        Assert.That(workOrder.Status, Is.EqualTo(NiiWorkOrderStatus.Running));
        Assert.That(workOrder.Machine, Is.EqualTo(machineUid));
        Assert.That(workOrder.Sample, Is.EqualTo(sampleUid));
        Assert.That(machine!.IsProcessing, Is.True);
        Assert.That(institute.EventLog.Count(eventState =>
            eventState.Type == NiiInstituteEventType.SampleDeliveryStarted), Is.EqualTo(1));
        Assert.That(institute.EventLog.Count(eventState =>
            eventState.Type == NiiInstituteEventType.ResearchStarted), Is.EqualTo(1));
        Assert.That(SEntMan.GetComponent<TransformComponent>(researcherUid).LocalPosition,
            Is.Not.EqualTo(researcherStartPosition));

        Assert.That(solutions.TryGetSolution(sampleUid, "beaker", out _, out var sampleSolution), Is.True);
        Assert.That(sampleSolution!.GetTotalPrototypeQuantity(project.RequiredReagent), Is.EqualTo(FixedPoint2.Zero));

        machine!.ElapsedSeconds = project.DurationSeconds;
        await Pair.RunTicksSync(2);
        Assert.That(institute.ResearchStatus, Is.EqualTo(NiiResearchStatus.Completed));
        Assert.That(workOrder.Status, Is.EqualTo(NiiWorkOrderStatus.Completed));
        Assert.That(institute.ActiveWorkOrder, Is.Null);
        Assert.That(institute.LastWorkOrder, Is.EqualTo(workOrderUid));
        Assert.That(laboratory.ActiveWorkOrder, Is.Null);
        Assert.That(SEntMan.GetComponent<NiiEmployeeComponent>(workOrder.AssignedTo!.Value).ActiveWorkOrder, Is.Null);
        Assert.That(institute.Science, Is.EqualTo(project.ScienceReward));
        Assert.That(institute.Reputation, Is.EqualTo(project.ReputationReward));
        Assert.That(institute.EventLog.Count(eventState =>
            eventState.Type == NiiInstituteEventType.ResearchCompleted), Is.EqualTo(1));
        Assert.That(institute.AiMessages[^1].Kind, Is.EqualTo(NiiAiMessageKind.Success));

        var delegationChanged = false;
        await Server.WaitPost(() => delegationChanged = terminalSystem.TrySetDelegatedAssignment(
            (instituteUid, institute), false));
        Assert.That(delegationChanged, Is.True);
        await Server.WaitPost(() => delegationChanged = terminalSystem.TrySetDelegatedAssignment(
            (instituteUid, institute), true));
        Assert.That(delegationChanged, Is.True);
        Assert.That(laboratory.AssignmentMode, Is.EqualTo(NiiLaboratoryAssignmentMode.Delegated));

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

        var sequences = institute.EventLog.Select(eventState => eventState.Sequence).ToArray();
        Assert.That(sequences, Is.Ordered.Ascending);
        Assert.That(sequences.Distinct().Count(), Is.EqualTo(sequences.Length));
        Assert.That(institute.EventLog.Any(eventState => eventState.Type == NiiInstituteEventType.DayAdvanced), Is.True);

        var narrative = Server.System<NiiInstituteNarrativeSystem>();
        var balanceBeforeNarration = institute.Balance;
        await Server.WaitPost(() =>
        {
            for (var i = 0; i < NiiInstituteNarrativeSystem.MaximumEventEntries + 8; i++)
            {
                narrative.Record(
                    (instituteUid, institute),
                    NiiInstituteEventType.ResearchCompleted,
                    NiiInstituteEventSeverity.Success,
                    new NiiInstituteEventData(Amount: i));
            }
        });
        Assert.That(institute.Balance, Is.EqualTo(balanceBeforeNarration));
        Assert.That(institute.EventLog, Has.Count.EqualTo(NiiInstituteNarrativeSystem.MaximumEventEntries));
        Assert.That(institute.AiMessages, Has.Count.EqualTo(NiiInstituteNarrativeSystem.MaximumAiMessageEntries));
        var boundedContext = contextSystem.Build((instituteUid, institute));
        Assert.That(boundedContext.RecentEvents, Has.Length.EqualTo(NiiInstituteAiContextSystem.MaximumRecentEvents));
        Assert.That(contextSystem.Serialize(boundedContext).Length, Is.LessThan(16_384));

        await Server.WaitPost(() => ticker.RestartRound());
    }
}
