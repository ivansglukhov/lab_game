using Content.Server.Nii.Components;
using Content.Shared.Nii;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Owns the bounded, versioned stream of authoritative institute facts and the local fallback narrator.
/// It never changes simulation state outside the two narrative buffers.
/// </summary>
public sealed partial class NiiInstituteNarrativeSystem : EntitySystem
{
    public const int SchemaVersion = 1;
    public const int MaximumEventEntries = 64;
    public const int MaximumAiMessageEntries = 12;

    [Dependency] private NiiInstituteChatSystem _chat = default!;

    public NiiInstituteEventState Record(
        Entity<NiiInstituteComponent> institute,
        NiiInstituteEventType type,
        NiiInstituteEventSeverity severity = NiiInstituteEventSeverity.Info,
        NiiInstituteEventData data = default)
    {
        var secondsIntoDay = GetSecondsIntoDay(institute.Comp);
        var actorId = GetPublicEntityId(data.Actor, "actor");
        var actorName = data.Actor is { } actorUid && Exists(actorUid)
            ? MetaData(actorUid).EntityName
            : string.Empty;
        var workOrderId = GetPublicEntityId(data.WorkOrder, "order");
        var sequence = institute.Comp.NextEventSequence++;
        var eventState = new NiiInstituteEventState(
            SchemaVersion,
            sequence,
            institute.Comp.CurrentDay,
            secondsIntoDay,
            type,
            severity,
            actorId,
            actorName,
            data.LaboratoryId ?? string.Empty,
            $"{institute.Comp.ActiveProject}",
            workOrderId,
            data.WorkOrderStatus,
            data.BlockReason,
            data.AssignmentMode,
            data.Amount,
            TechnicalText(type, actorName, data));

        institute.Comp.EventLog.Add(eventState);
        TrimOldest(institute.Comp.EventLog, MaximumEventEntries);
        _chat.SendEvent(eventState);

        if (NarratorText(type, actorName, data) is { } narratorText)
        {
            var kind = severity switch
            {
                NiiInstituteEventSeverity.Attention or NiiInstituteEventSeverity.Critical => NiiAiMessageKind.Alert,
                NiiInstituteEventSeverity.Success => NiiAiMessageKind.Success,
                _ => NiiAiMessageKind.Report,
            };
            var aiMessage = new NiiAiMessageState(
                SchemaVersion,
                institute.Comp.NextAiMessageSequence++,
                eventState.Sequence,
                eventState.Day,
                eventState.SecondsIntoDay,
                kind,
                severity,
                narratorText);
            institute.Comp.AiMessages.Add(aiMessage);
            TrimOldest(institute.Comp.AiMessages, MaximumAiMessageEntries);
            _chat.SendAiMessage(aiMessage);
        }

        return eventState;
    }

    private string TechnicalText(
        NiiInstituteEventType type,
        string actorName,
        NiiInstituteEventData data)
    {
        return type switch
        {
            NiiInstituteEventType.InstituteStarted => Loc.GetString("nii-event-institute-started"),
            NiiInstituteEventType.DayAdvanced => Loc.GetString("nii-event-day-advanced", ("amount", data.Amount)),
            NiiInstituteEventType.FinancialWarning => Loc.GetString("nii-event-financial-warning", ("amount", data.Amount)),
            NiiInstituteEventType.ProjectAuthorized => Loc.GetString("nii-event-project-authorized"),
            NiiInstituteEventType.WorkOrderCreated => Loc.GetString("nii-event-work-order-created"),
            NiiInstituteEventType.AssignmentModeChanged => Loc.GetString(
                data.AssignmentMode == NiiLaboratoryAssignmentMode.Delegated
                    ? "nii-event-assignment-delegated"
                    : "nii-event-assignment-manual"),
            NiiInstituteEventType.EmployeeAssigned => Loc.GetString("nii-event-employee-assigned", ("employee", actorName)),
            NiiInstituteEventType.SampleReserved => Loc.GetString("nii-event-sample-reserved", ("employee", actorName)),
            NiiInstituteEventType.SampleDeliveryStarted => Loc.GetString("nii-event-sample-delivery", ("employee", actorName)),
            NiiInstituteEventType.ResearchStarted => Loc.GetString("nii-event-research-started"),
            NiiInstituteEventType.ResearchCompleted => Loc.GetString("nii-event-research-completed"),
            NiiInstituteEventType.ResourceShortage => Loc.GetString(
                "nii-event-resource-shortage",
                ("reason", Loc.GetString(BlockReasonLocId(data.BlockReason)))),
            NiiInstituteEventType.WorkOrderBlocked => Loc.GetString(
                "nii-event-work-order-blocked",
                ("reason", Loc.GetString(BlockReasonLocId(data.BlockReason)))),
            NiiInstituteEventType.WorkOrderRecovered => Loc.GetString("nii-event-work-order-recovered"),
            NiiInstituteEventType.EmployeeUnavailable => Loc.GetString("nii-event-employee-unavailable", ("employee", actorName)),
            _ => type.ToString(),
        };
    }

    private string? NarratorText(
        NiiInstituteEventType type,
        string actorName,
        NiiInstituteEventData data)
    {
        return type switch
        {
            NiiInstituteEventType.InstituteStarted => Loc.GetString("nii-ai-institute-started"),
            NiiInstituteEventType.FinancialWarning => Loc.GetString("nii-ai-financial-warning", ("amount", data.Amount)),
            NiiInstituteEventType.ProjectAuthorized => Loc.GetString("nii-ai-project-authorized"),
            NiiInstituteEventType.AssignmentModeChanged => Loc.GetString(
                data.AssignmentMode == NiiLaboratoryAssignmentMode.Delegated
                    ? "nii-ai-assignment-delegated"
                    : "nii-ai-assignment-manual"),
            NiiInstituteEventType.EmployeeAssigned => Loc.GetString("nii-ai-employee-assigned", ("employee", actorName)),
            NiiInstituteEventType.ResearchStarted => Loc.GetString("nii-ai-research-started"),
            NiiInstituteEventType.ResearchCompleted => Loc.GetString("nii-ai-research-completed"),
            NiiInstituteEventType.ResourceShortage => Loc.GetString(
                "nii-ai-resource-shortage",
                ("reason", Loc.GetString(BlockReasonLocId(data.BlockReason)))),
            NiiInstituteEventType.WorkOrderBlocked => Loc.GetString(
                "nii-ai-work-order-blocked",
                ("reason", Loc.GetString(BlockReasonLocId(data.BlockReason)))),
            NiiInstituteEventType.WorkOrderRecovered => Loc.GetString("nii-ai-work-order-recovered"),
            NiiInstituteEventType.EmployeeUnavailable => Loc.GetString("nii-ai-employee-unavailable", ("employee", actorName)),
            _ => null,
        };
    }

    private string GetPublicEntityId(EntityUid? uid, string prefix)
    {
        return uid is { } entity && Exists(entity)
            ? $"{prefix}-{GetNetEntity(entity).Id}"
            : string.Empty;
    }

    private static int GetSecondsIntoDay(NiiInstituteComponent institute)
    {
        if (institute.DayDurationSeconds <= 0f)
            return 0;

        var progress = Math.Clamp(institute.ElapsedSeconds / institute.DayDurationSeconds, 0f, 1f);
        return (int) (progress * 86_399f);
    }

    private static void TrimOldest<T>(List<T> entries, int maximum)
    {
        if (entries.Count > maximum)
            entries.RemoveRange(0, entries.Count - maximum);
    }

    private static string BlockReasonLocId(NiiWorkOrderBlockReason reason)
    {
        return reason switch
        {
            NiiWorkOrderBlockReason.NoSample => "nii-work-order-block-no-sample",
            NiiWorkOrderBlockReason.SampleInaccessible => "nii-work-order-block-sample-inaccessible",
            NiiWorkOrderBlockReason.MachineBusy => "nii-work-order-block-machine-busy",
            NiiWorkOrderBlockReason.MachineInaccessible => "nii-work-order-block-machine-inaccessible",
            NiiWorkOrderBlockReason.EmployeeUnavailable => "nii-work-order-block-employee-unavailable",
            _ => "nii-work-order-block-none",
        };
    }
}

public readonly record struct NiiInstituteEventData(
    EntityUid? Actor = null,
    EntityUid? WorkOrder = null,
    string? LaboratoryId = null,
    NiiWorkOrderStatus WorkOrderStatus = NiiWorkOrderStatus.Created,
    NiiWorkOrderBlockReason BlockReason = NiiWorkOrderBlockReason.None,
    NiiLaboratoryAssignmentMode AssignmentMode = NiiLaboratoryAssignmentMode.Manual,
    int Amount = 0);
