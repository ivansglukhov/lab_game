using Content.Server.Nii.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Nii;
using Content.Shared.Nii.Prototypes;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Runs the physical half of NII research: reagent loading, timed work and result production.
/// </summary>
public sealed partial class NiiResearchMachineSystem : EntitySystem
{
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private NiiDirectorTerminalSystem _terminals = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NiiResearchMachineComponent, AfterInteractUsingEvent>(OnInteractUsing);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NiiResearchMachineComponent>();
        while (query.MoveNext(out var uid, out var machine))
        {
            if (!machine.IsProcessing || machine.Institute is not { } instituteUid ||
                !TryComp<NiiInstituteComponent>(instituteUid, out var institute))
                continue;

            var project = _prototypes.Index(institute.ActiveProject);
            machine.ElapsedSeconds += frameTime;
            if (machine.ElapsedSeconds < project.DurationSeconds)
                continue;

            CompleteResearch(uid, machine, institute, project);
        }
    }

    private void OnInteractUsing(
        Entity<NiiResearchMachineComponent> machine,
        ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach)
            return;

        args.Handled = TryStartResearch(machine, args.Used, args.User);
    }

    public bool TryStartResearch(
        Entity<NiiResearchMachineComponent> machine,
        EntityUid sample,
        EntityUid user)
    {
        if (machine.Comp.IsProcessing)
            return false;

        var institutes = EntityQueryEnumerator<NiiInstituteComponent>();
        if (!institutes.MoveNext(out var instituteUid, out var institute) ||
            institute.ResearchStatus != NiiResearchStatus.Authorized)
        {
            _popup.PopupEntity(Loc.GetString("nii-research-machine-not-authorized"), machine, user);
            return false;
        }

        var project = _prototypes.Index(institute.ActiveProject);
        if (!_solutions.TryGetSolution(sample, machine.Comp.SolutionName, out var solutionEntity, out var solution) ||
            solution.GetTotalPrototypeQuantity(project.RequiredReagent) < project.RequiredReagentAmount)
        {
            _popup.PopupEntity(
                Loc.GetString("nii-research-machine-insufficient-reagent",
                    ("amount", project.RequiredReagentAmount.Int())),
                machine,
                user);
            return false;
        }

        _solutions.RemoveReagent(solutionEntity.Value, project.RequiredReagent, project.RequiredReagentAmount);
        machine.Comp.IsProcessing = true;
        machine.Comp.ElapsedSeconds = 0f;
        machine.Comp.Institute = instituteUid;
        institute.ResearchStatus = NiiResearchStatus.Running;
        NiiInstituteSystem.AddEvent(institute, NiiInstituteEventType.ResearchStarted);
        _terminals.RefreshAll(institute);
        _popup.PopupEntity(Loc.GetString("nii-research-machine-started"), machine, user);
        return true;
    }

    private void CompleteResearch(
        EntityUid uid,
        NiiResearchMachineComponent machine,
        NiiInstituteComponent institute,
        NiiResearchProjectPrototype project)
    {
        machine.IsProcessing = false;
        machine.ElapsedSeconds = 0f;
        machine.Institute = null;
        institute.ResearchStatus = NiiResearchStatus.Completed;
        institute.Science += project.ScienceReward;
        institute.Reputation += project.ReputationReward;
        NiiInstituteSystem.AddEvent(institute, NiiInstituteEventType.ResearchCompleted);
        Spawn(project.ResultPrototype, Transform(uid).Coordinates);
        _terminals.RefreshAll(institute);
    }
}
