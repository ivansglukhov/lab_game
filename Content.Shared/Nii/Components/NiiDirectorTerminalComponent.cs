using Content.Shared.Nii;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared.Nii.Components;

/// <summary>
/// Marks the physical terminal used to inspect and manage the institute.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class NiiDirectorTerminalComponent : Component
{
}

[Serializable, NetSerializable]
public enum NiiDirectorTerminalUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class NiiDirectorTerminalBuiState : BoundUserInterfaceState
{
    public int CurrentDay { get; }
    public int Balance { get; }
    public int DailyFunding { get; }
    public int DailyExpenses { get; }
    public int Reputation { get; }
    public int Science { get; }
    public bool IsBankrupt { get; }
    public string ProjectName { get; }
    public int ProjectCost { get; }
    public int RequiredReagentAmount { get; }
    public int ProjectDurationSeconds { get; }
    public NiiResearchStatus ResearchStatus { get; }
    public bool CanAuthorize { get; }
    public NiiInstituteEventType[] EventLog { get; }

    public NiiDirectorTerminalBuiState(
        int currentDay,
        int balance,
        int dailyFunding,
        int dailyExpenses,
        int reputation,
        int science,
        bool isBankrupt,
        string projectName,
        int projectCost,
        int requiredReagentAmount,
        int projectDurationSeconds,
        NiiResearchStatus researchStatus,
        bool canAuthorize,
        NiiInstituteEventType[] eventLog)
    {
        CurrentDay = currentDay;
        Balance = balance;
        DailyFunding = dailyFunding;
        DailyExpenses = dailyExpenses;
        Reputation = reputation;
        Science = science;
        IsBankrupt = isBankrupt;
        ProjectName = projectName;
        ProjectCost = projectCost;
        RequiredReagentAmount = requiredReagentAmount;
        ProjectDurationSeconds = projectDurationSeconds;
        ResearchStatus = researchStatus;
        CanAuthorize = canAuthorize;
        EventLog = eventLog;
    }
}

[Serializable, NetSerializable]
public sealed class NiiAuthorizeResearchMessage : BoundUserInterfaceMessage;
