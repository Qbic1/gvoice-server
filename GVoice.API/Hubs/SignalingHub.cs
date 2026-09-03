using GVoice.API.Models;
using GVoice.API.Services;
using Microsoft.AspNetCore.SignalR;

namespace GVoice.API.Hubs;

public partial class SignalingHub(
    ILogger<SignalingHub> logger,
    IConfiguration configuration,
    XmlChatHistoryService chatHistoryService,
    IRoomService roomService,
    IParticipantService participantService) : Hub
{
    private readonly string _adminPassword = configuration["AdminPassword"] ?? "default-secret";
    private readonly XmlChatHistoryService _chatHistoryService = chatHistoryService;

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        await LeaveCurrentRoomAsync(Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }

    private async Task LeaveCurrentRoomAsync(string connectionId)
    {
        var participant = participantService.Remove(connectionId);

        if (participant is null)
        {
            return;
        }

        var room = roomService.Get(participant.RoomId);
        room?.Participants.TryRemove(connectionId, out _);

        logger.LogInformation("Participant {Name} left room {RoomId}",
            participant.DisplayName, participant.RoomId);

        await Groups.RemoveFromGroupAsync(connectionId, participant.RoomId);

        await Clients.Group(participant.RoomId)
            .SendAsync(SignalREvents.PeerLeft, connectionId, participant.DisplayName);
    }

    public async Task Join(string roomId, string password, string displayName, bool isListenOnly, string avatar = "")
    {
        var room = await ValidateRoomJoin(roomId, password);
        if (room is null) return;

        // Clean up any previous room membership on this connection so the
        // participant is never left as a ghost in another room / SignalR group.
        if (participantService.Get(Context.ConnectionId) is not null)
        {
            await LeaveCurrentRoomAsync(Context.ConnectionId);
        }

        displayName = Sanitize(displayName, 20);

        var participant = new Participant
        {
            ConnectionId = Context.ConnectionId,
            RoomId = roomId,
            DisplayName = displayName,
            IsListenOnly = isListenOnly,
            Avatar = NormalizeAvatar(avatar)
        };

        if (!roomService.Join(room.Id, participant))
        {
            logger.LogWarning("Join failed for {ConnectionId} in room {RoomId}",
                Context.ConnectionId, room.Id);
            await Clients.Caller.SendAsync(SignalREvents.JoinFailed);
            return;
        }

        participantService.CreateOrUpdate(participant);

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        await SendJoinData(room, participant);
    }

    public async Task SendSignal(string targetConnectionId, string signal)
    {
        var sender = participantService.Get(Context.ConnectionId);

        var receiver = participantService.Get(targetConnectionId);

        if (sender is null ||
            receiver is null ||
            sender.RoomId != receiver.RoomId)
        {
            return;
        }

        await Clients.Client(targetConnectionId)
            .SendAsync(SignalREvents.ReceiveSignal, Context.ConnectionId, signal);
    }

    public async Task SendChatMessage(string message)
    {
        var sender = participantService.Get(Context.ConnectionId);

        if (sender is null)
            return;

        var room = roomService.Get(sender.RoomId);

        if (room is null)
            return;

        var sanitized = Sanitize(message);

        if (string.IsNullOrEmpty(sanitized))
            return;

        var chatMessage = new ChatMessage
        {
            DisplayName = sender.DisplayName,
            Message = sanitized,
            Timestamp = DateTime.UtcNow
        };

        await _chatHistoryService.WriteMessageAsync(sender.RoomId, chatMessage);

        await Clients.Group(sender.RoomId)
            .SendAsync(SignalREvents.ReceiveChatMessage,
                chatMessage.DisplayName,
                chatMessage.Message,
                chatMessage.Timestamp);
    }

    public async Task UpdateState(string stateType, bool value)
    {
        var participant = participantService.Get(Context.ConnectionId);
        if (participant is null) return;

        var applied = stateType switch
        {
            SignalREvents.Muted => participantService.SetMuted(Context.ConnectionId, value),
            SignalREvents.Deafened => participantService.SetDeafened(Context.ConnectionId, value),
            SignalREvents.SharingScreen => participantService.SetSharingScreen(Context.ConnectionId, value),
            _ => false
        };

        if (!applied) return;

        await Clients.Group(participant.RoomId)
            .SendAsync(SignalREvents.PeerStateUpdated, Context.ConnectionId, stateType, value);
    }

    public async Task CreateRoom(string adminPassword, string roomName, string roomPassword)
    {
        if (adminPassword != _adminPassword)
        {
            logger.LogWarning("Invalid admin password");
            return;
        }

        var room = roomService.Create(roomName, roomPassword);

        logger.LogInformation("Room created: {RoomId}", room.Id);

        // SECURITY: never broadcast the full Room object — it carries the plaintext
        // Password (and participant list). Clients only need id + name for the lobby.
        await Clients.All.SendAsync(SignalREvents.RoomCreated, new { room.Id, room.Name });
    }

    private async Task<Room?> ValidateRoomJoin(string roomId, string password)
    {
        var room = roomService.Get(roomId);

        if (room is null)
        {
            await Clients.Caller.SendAsync(SignalREvents.RoomNotFound);
            return null;
        }

        if (!roomService.IsPasswordCorrect(roomId, password))
        {
            await Clients.Caller.SendAsync(SignalREvents.InvalidPassword);
            return null;
        }

        if (roomService.IsRoomFull(roomId))
        {
            await Clients.Caller.SendAsync(SignalREvents.RoomFull);
            return null;
        }

        return room;
    }

    private async Task SendJoinData(Room room, Participant participant)
    {
        var history = await _chatHistoryService.ReadHistoryAsync(room.Id);

        await Clients.Caller.SendAsync(SignalREvents.ReceiveChatHistory, history);

        await Clients.OthersInGroup(room.Id)
            .SendAsync(SignalREvents.PeerJoined, participant);

        await Clients.Caller.SendAsync(SignalREvents.RoomJoined, new
        {
            room.Name,
            Participants = room.Participants.Values.ToList()
        });
    }

    // Avatar rides its own method rather than UpdateState: that hub method is
    // typed (string, bool) and its switch drops unknown keys, so a string-valued
    // avatar cannot travel on it without breaking its three existing callers.
    public async Task UpdateAvatar(string avatar)
    {
        var participant = participantService.Get(Context.ConnectionId);
        if (participant is null) return;

        var normalized = NormalizeAvatar(avatar);

        if (!participantService.SetAvatar(Context.ConnectionId, normalized)) return;

        await Clients.Group(participant.RoomId)
            .SendAsync(SignalREvents.AvatarUpdated, Context.ConnectionId, normalized);
    }

    // Shape-only whitelist. The avatar catalogue lives in the client; keeping a
    // copy here would be a third place to keep in sync, so anything that is not a
    // plausible slug becomes empty and the client derives a default from the name.
    private static string NormalizeAvatar(string? avatar)
    {
        var value = avatar?.Trim() ?? "";
        return AvatarSlugPattern().IsMatch(value) ? value : "";
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z][a-z0-9-]{0,23}$")]
    private static partial System.Text.RegularExpressions.Regex AvatarSlugPattern();

    private static string Sanitize(string? input, int maxLength = int.MaxValue)
    {
        var value = System.Net.WebUtility.HtmlEncode(input?.Trim() ?? "");
        return value.Length > maxLength ? value[..maxLength] : value;
    }
}
