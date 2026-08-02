using System.Linq;
using Content.Server.Chat.Managers;
using Content.Server.Mind;
using Content.Server.Nii.Components;
using Content.Shared.Chat;
using Content.Shared.GameTicking;
using Content.Shared.Nii;
using Content.Shared.Roles;
using Content.Shared.Roles.Jobs;
using Robust.Server.Player;
using Robust.Shared.Maths;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server.Nii.Systems;

/// <summary>
/// Delivers institute facts, narrator reports and director commands to the director's regular chat feed.
/// </summary>
public sealed partial class NiiInstituteChatSystem : EntitySystem
{
    public static readonly ProtoId<JobPrototype> DirectorJob = "NiiDirector";

    private static readonly Color InstituteColor = Color.FromHex("#AFC4D4");
    private static readonly Color AiReportColor = Color.FromHex("#70D6E5");
    private static readonly Color AiAlertColor = Color.FromHex("#FFB45E");
    private static readonly Color AiSuccessColor = Color.FromHex("#78D99B");
    private static readonly Color CommandColor = Color.FromHex("#D7B56D");

    [Dependency] private IChatManager _chat = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private MindSystem _minds = default!;
    [Dependency] private SharedJobSystem _jobs = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawned);
    }

    public void SendEvent(NiiInstituteEventState eventState)
    {
        var message = Loc.GetString(
            "nii-chat-institute-message",
            ("day", eventState.Day),
            ("time", FormatTime(eventState.SecondsIntoDay)),
            ("message", eventState.Message));
        SendToDirectors(message, InstituteColor);
    }

    public void SendAiMessage(NiiAiMessageState messageState)
    {
        var message = Loc.GetString("nii-chat-ai-message", ("message", messageState.Text));
        var color = messageState.Kind switch
        {
            NiiAiMessageKind.Alert => AiAlertColor,
            NiiAiMessageKind.Success => AiSuccessColor,
            _ => AiReportColor,
        };
        SendToDirectors(message, color);
    }

    public void SendDirectorCommand(EntityUid actor, string command)
    {
        if (!_players.TryGetSessionByEntity(actor, out var session) || !IsDirector(session))
            return;

        var message = Loc.GetString("nii-chat-director-command", ("command", command));
        Send(session, message, CommandColor);
    }

    public bool IsDirector(ICommonSession session)
    {
        return _minds.TryGetMind(session.UserId, out var mindId, out _) &&
               _jobs.MindTryGetJobId(mindId, out var jobId) &&
               jobId == DirectorJob;
    }

    public IReadOnlyList<ICommonSession> GetDirectorRecipients()
    {
        return _players.Sessions.Where(IsDirector).ToArray();
    }

    private void OnPlayerSpawned(PlayerSpawnCompleteEvent args)
    {
        if (args.JobId != DirectorJob.Id)
            return;

        var query = EntityQueryEnumerator<NiiInstituteComponent>();
        if (!query.MoveNext(out _, out var institute))
            return;

        foreach (var eventState in institute.EventLog.TakeLast(6))
        {
            SendEventTo(args.Player, eventState);

            foreach (var aiMessage in institute.AiMessages.Where(message =>
                         message.RelatedEventSequence == eventState.Sequence))
            {
                SendAiMessageTo(args.Player, aiMessage);
            }
        }
    }

    private void SendToDirectors(string message, Color color)
    {
        foreach (var recipient in GetDirectorRecipients())
        {
            Send(recipient, message, color);
        }
    }

    private void SendEventTo(ICommonSession recipient, NiiInstituteEventState eventState)
    {
        var message = Loc.GetString(
            "nii-chat-institute-message",
            ("day", eventState.Day),
            ("time", FormatTime(eventState.SecondsIntoDay)),
            ("message", eventState.Message));
        Send(recipient, message, InstituteColor);
    }

    private void SendAiMessageTo(ICommonSession recipient, NiiAiMessageState messageState)
    {
        var message = Loc.GetString("nii-chat-ai-message", ("message", messageState.Text));
        var color = messageState.Kind switch
        {
            NiiAiMessageKind.Alert => AiAlertColor,
            NiiAiMessageKind.Success => AiSuccessColor,
            _ => AiReportColor,
        };
        Send(recipient, message, color);
    }

    private void Send(ICommonSession recipient, string message, Color color)
    {
        var wrappedMessage = Loc.GetString(
            "chat-manager-server-wrap-message",
            ("message", FormattedMessage.EscapeText(message)));
        _chat.ChatMessageToOne(
            ChatChannel.Server,
            message,
            wrappedMessage,
            default,
            false,
            recipient.Channel,
            color);
    }

    private static string FormatTime(int secondsIntoDay)
    {
        var hours = secondsIntoDay / 3600;
        var minutes = secondsIntoDay % 3600 / 60;
        return $"{hours:00}:{minutes:00}";
    }
}
