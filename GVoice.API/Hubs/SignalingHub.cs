using GVoice.API.Models;
using GVoice.API.Services;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace GVoice.API.Hubs;

public partial class SignalingHub(
    ILogger<SignalingHub> logger,
    IConfiguration configuration,
    XmlChatHistoryService chatHistoryService,
    IRoomService roomService) : Hub
{
    private static readonly ConcurrentDictionary<string, Participant> Participants = new();
    private readonly string _adminPassword = configuration["AdminPassword"] ?? "default-secret";
    private readonly XmlChatHistoryService _chatHistoryService = chatHistoryService;

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (!Participants.TryRemove(Context.ConnectionId, out var participant))
            return;

        var room = roomService.Get(participant.RoomId);

        if (room is null)
        {
            return;
        }

        room.Participants.TryRemove(Context.ConnectionId, out _);

        logger.LogInformation("Participant {Name} left room {RoomId}",
            participant.DisplayName, participant.RoomId);

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, participant.RoomId);

        await Clients.Group(participant.RoomId)
            .SendAsync(SignalREvents.PeerLeft, Context.ConnectionId, participant.DisplayName);

        await base.OnDisconnectedAsync(exception);
    }

    public async Task Join(string roomId, string password, string displayName, bool isListenOnly)
    {
        var room = ValidateRoomJoin(roomId, password);
        if (room is null) return;

        displayName = Sanitize(displayName, 20);

        var participant = new Participant
        {
            ConnectionId = Context.ConnectionId,
            RoomId = roomId,
            DisplayName = displayName,
            IsListenOnly = isListenOnly
        };

        if (!room.Participants.TryAdd(Context.ConnectionId, participant))
            return;

        Participants[Context.ConnectionId] = participant;

        await Groups.AddToGroupAsync(Context.ConnectionId, roomId);

        await SendJoinData(room, participant);
    }

    public async Task SendSignal(string targetConnectionId, string signal)
    {
        if (!Participants.TryGetValue(Context.ConnectionId, out var sender) ||
            !Participants.TryGetValue(targetConnectionId, out var receiver) ||
            sender.RoomId != receiver.RoomId)
            return;

        await Clients.Client(targetConnectionId)
            .SendAsync(SignalREvents.ReceiveSignal, Context.ConnectionId, signal);
    }

    public async Task SendChatMessage(string message)
    {
        if (!Participants.TryGetValue(Context.ConnectionId, out var sender))
            return;

        var sanitized = Sanitize(message);
        if (string.IsNullOrEmpty(sanitized)) return;

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
        if (!Participants.TryGetValue(Context.ConnectionId, out var participant))
            return;

        if (!ApplyState(participant, stateType, value))
            return;

        await Clients.Group(participant.RoomId)
            .SendAsync(SignalREvents.PeerStateUpdated,
                Context.ConnectionId, stateType, value);
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

        await Clients.All.SendAsync("RoomCreated", room);
    }

    private Room? ValidateRoomJoin(string roomId, string password)
    {
        var room = roomService.Get(roomId);

        if (room is null)
        {
            Clients.Caller.SendAsync(SignalREvents.RoomNotFound);
            return null;
        }

        if (!roomService.IsPasswordCorrect(roomId, password))
        {
            Clients.Caller.SendAsync(SignalREvents.InvalidPassword);
            return null;
        }

        if (roomService.IsRoomFull(roomId))
        {
            Clients.Caller.SendAsync(SignalREvents.RoomFull);
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

    private static string Sanitize(string? input, int maxLength = int.MaxValue)
    {
        var value = System.Net.WebUtility.HtmlEncode(input?.Trim() ?? "");
        return value.Length > maxLength ? value[..maxLength] : value;
    }

    private static bool ApplyState(Participant participant, string stateType, bool value)
    {
        return stateType switch
        {
            SignalREvents.Muted => (participant.IsMuted = value) == value,
            SignalREvents.Deafened => (participant.IsDeafened = value) == value,
            _ => false
        };
    }
}
