using Robust.Shared.Serialization;

namespace Content.Shared.Nii;

/// <summary>
/// Versioned, immutable fact emitted by the authoritative institute simulation.
/// </summary>
[Serializable, NetSerializable]
public sealed class NiiInstituteEventState(
    int schemaVersion,
    ulong sequence,
    int day,
    int secondsIntoDay,
    NiiInstituteEventType type,
    NiiInstituteEventSeverity severity,
    string actorId,
    string actorName,
    string laboratoryId,
    string projectId,
    string workOrderId,
    NiiWorkOrderStatus workOrderStatus,
    NiiWorkOrderBlockReason blockReason,
    NiiLaboratoryAssignmentMode assignmentMode,
    int amount,
    string message)
{
    public int SchemaVersion { get; } = schemaVersion;
    public ulong Sequence { get; } = sequence;
    public int Day { get; } = day;
    public int SecondsIntoDay { get; } = secondsIntoDay;
    public NiiInstituteEventType Type { get; } = type;
    public NiiInstituteEventSeverity Severity { get; } = severity;
    public string ActorId { get; } = actorId;
    public string ActorName { get; } = actorName;
    public string LaboratoryId { get; } = laboratoryId;
    public string ProjectId { get; } = projectId;
    public string WorkOrderId { get; } = workOrderId;
    public NiiWorkOrderStatus WorkOrderStatus { get; } = workOrderStatus;
    public NiiWorkOrderBlockReason BlockReason { get; } = blockReason;
    public NiiLaboratoryAssignmentMode AssignmentMode { get; } = assignmentMode;
    public int Amount { get; } = amount;
    public string Message { get; } = message;
}

/// <summary>
/// A narrator-facing message derived exclusively from one authoritative event.
/// </summary>
[Serializable, NetSerializable]
public sealed class NiiAiMessageState(
    int schemaVersion,
    ulong sequence,
    ulong relatedEventSequence,
    int day,
    int secondsIntoDay,
    NiiAiMessageKind kind,
    NiiInstituteEventSeverity priority,
    string text)
{
    public int SchemaVersion { get; } = schemaVersion;
    public ulong Sequence { get; } = sequence;
    public ulong RelatedEventSequence { get; } = relatedEventSequence;
    public int Day { get; } = day;
    public int SecondsIntoDay { get; } = secondsIntoDay;
    public NiiAiMessageKind Kind { get; } = kind;
    public NiiInstituteEventSeverity Priority { get; } = priority;
    public string Text { get; } = text;
}
