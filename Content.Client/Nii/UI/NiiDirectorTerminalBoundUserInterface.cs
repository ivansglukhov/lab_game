using Content.Shared.Nii.Components;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client.Nii.UI;

[UsedImplicitly]
public sealed class NiiDirectorTerminalBoundUserInterface(EntityUid owner, Enum uiKey)
    : BoundUserInterface(owner, uiKey)
{
    private NiiDirectorTerminalWindow? _window;

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<NiiDirectorTerminalWindow>();
        _window.AuthorizeProjectRequested += OnAuthorizeProjectRequested;
        _window.AssignResearcherRequested += OnAssignResearcherRequested;
        _window.DelegatedAssignmentRequested += OnDelegatedAssignmentRequested;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is NiiDirectorTerminalBuiState terminalState)
            _window?.UpdateState(terminalState);
    }

    private void OnAuthorizeProjectRequested()
    {
        SendMessage(new NiiAuthorizeResearchMessage());
    }

    private void OnAssignResearcherRequested(NetEntity employee)
    {
        SendMessage(new NiiAssignResearcherMessage(employee));
    }

    private void OnDelegatedAssignmentRequested(bool enabled)
    {
        SendMessage(new NiiSetDelegatedAssignmentMessage(enabled));
    }
}
