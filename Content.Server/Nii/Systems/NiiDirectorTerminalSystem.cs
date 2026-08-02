using Content.Server.Nii.Components;
using Content.Shared.Nii;
using Content.Shared.Nii.Components;
using Content.Shared.Nii.Prototypes;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Supplies authoritative institute data to all open director terminals.
/// </summary>
public sealed partial class NiiDirectorTerminalSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NiiDirectorTerminalComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
        SubscribeLocalEvent<NiiDirectorTerminalComponent, NiiAuthorizeResearchMessage>(OnAuthorizeResearch);
    }

    private void OnAuthorizeResearch(
        Entity<NiiDirectorTerminalComponent> terminal,
        ref NiiAuthorizeResearchMessage args)
    {
        if (args.Actor is not { Valid: true })
            return;

        var query = EntityQueryEnumerator<NiiInstituteComponent>();
        if (!query.MoveNext(out _, out var institute))
            return;

        TryAuthorizeResearch(institute);
    }

    public bool TryAuthorizeResearch(NiiInstituteComponent institute)
    {
        var project = _prototypes.Index(institute.ActiveProject);
        if (institute.ResearchStatus != NiiResearchStatus.Available || institute.Balance < project.Cost)
            return false;

        institute.Balance -= project.Cost;
        institute.ResearchStatus = NiiResearchStatus.Authorized;
        institute.IsBankrupt = institute.Balance < 0;
        NiiInstituteSystem.AddEvent(institute, NiiInstituteEventType.ProjectAuthorized);
        RefreshAll(institute);
        return true;
    }

    private void OnBeforeUiOpen(
        Entity<NiiDirectorTerminalComponent> terminal,
        ref BeforeActivatableUIOpenEvent args)
    {
        var query = EntityQueryEnumerator<NiiInstituteComponent>();
        if (query.MoveNext(out _, out var institute))
            SetState(terminal, institute);
    }

    public void RefreshAll(NiiInstituteComponent institute)
    {
        var query = EntityQueryEnumerator<NiiDirectorTerminalComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            SetState(uid, institute);
        }
    }

    private void SetState(EntityUid terminal, NiiInstituteComponent institute)
    {
        var project = _prototypes.Index(institute.ActiveProject);
        _ui.SetUiState(
            terminal,
            NiiDirectorTerminalUiKey.Key,
            new NiiDirectorTerminalBuiState(
                institute.CurrentDay,
                institute.Balance,
                institute.DailyFunding,
                institute.DailyExpenses,
                institute.Reputation,
                institute.Science,
                institute.IsBankrupt,
                Loc.GetString(project.Name),
                project.Cost,
                project.RequiredReagentAmount.Int(),
                (int) project.DurationSeconds,
                institute.ResearchStatus,
                institute.ResearchStatus == NiiResearchStatus.Available && institute.Balance >= project.Cost,
                institute.EventLog.ToArray()));
    }
}
