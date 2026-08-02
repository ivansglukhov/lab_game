using Content.Server.Nii.Components;
using Content.Shared.Nii.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Supplies authoritative institute data to all open director terminals.
/// </summary>
public sealed partial class NiiDirectorTerminalSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<NiiDirectorTerminalComponent, BeforeActivatableUIOpenEvent>(OnBeforeUiOpen);
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
                institute.IsBankrupt));
    }
}
