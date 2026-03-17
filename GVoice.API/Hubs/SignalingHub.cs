using GVoice.API.Models;
using GVoice.API.Services;
using Microsoft.AspNetCore.SignalR;
using System.Collections.Concurrent;

namespace GVoice.API.Hubs;

public class SignalingHub(
    ILogger<SignalingHub> logger, 
    IConfiguration configuration,
    XmlChatHistoryService chatHistoryService
    ) : Hub
{
    private static readonly ConcurrentDictionary<string, Room> Rooms = new();
    private const int MaxUsersPerRoom = 10;
    private readonly ILogger<SignalingHub> _logger = logger;
    private readonly string _adminPassword = configuration["AdminPassword"] ?? "default-secret";
    private readonly XmlChatHistoryService _chatHistoryService = chatHistoryService;

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        string? targetRoomId = null;
        Participant? participant = null;

        foreach (var room in Rooms.Values)
        {
            if (room.Participants.TryRemove(Context.ConnectionId, out participant))
            {
                targetRoomId = room.Id;
                break;
            }
        }

        if (targetRoomId != null && participant != null)
        {
            _logger.LogInformation("Participant {DisplayName} left room {RoomId}.", 
                participant.DisplayName, targetRoomId);
            
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, targetRoomId);
            await Clients.Group(targetRoomId).SendAsync(SignalREvents.PeerLeft, Context.ConnectionId, participant.DisplayName);
        }

        await base.OnDisconnectedAsync(exception);
    }

    public async Task Join(string roomId, string roomPassword, string displayName, bool isListenOnly)
    {
        if (string.IsNullOrEmpty(roomId) || !Rooms.TryGetValue(roomId, out var room))
        {
            _logger.LogWarning("Join failed: Room {RoomId} does not exist or is invalid.", roomId);
            await Clients.Caller.SendAsync(SignalREvents.RoomNotFound);
            return;
        }

        if (string.IsNullOrEmpty(roomPassword) || room.Password != roomPassword)
        {
            _logger.LogWarning("Join failed: Incorrect or missing password for room {RoomId}.", roomId);
            await Clients.Caller.SendAsync(SignalREvents.InvalidPassword);
            return;
        }
        if (room.Participants.Count >= MaxUsersPerRoom)
        {
            _logger.LogWarning("Join failed: Room {RoomId} is full.", roomId);
            await Clients.Caller.SendAsync(SignalREvents.RoomFull);
            return;
        }

        // Basic XSS/Input Validation
        displayName = System.Net.WebUtility.HtmlEncode(displayName?.Trim() ?? "Anonymous");
        if (displayName.Length > 20) displayName = displayName[..20];

        var participant = new Participant
        {
            ConnectionId = Context.ConnectionId,
            RoomId = roomId,
            DisplayName = displayName,
            IsListenOnly = isListenOnly
        };

        if (room.Participants.TryAdd(Context.ConnectionId, participant))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            _logger.LogInformation("Participant {DisplayName} joined room {RoomId}.", displayName, roomId);

            // Send history before announcing the join
            var history = await _chatHistoryService.ReadHistoryAsync(roomId);
            await Clients.Caller.SendAsync(SignalREvents.ReceiveChatHistory, history);

            await Clients.OthersInGroup(roomId).SendAsync(SignalREvents.PeerJoined, participant);
            var allInRoom = room.Participants.Values.ToList();
            await Clients.Caller.SendAsync(SignalREvents.RoomJoined, new { room.Name, Participants = allInRoom });
        }
    }

    public async Task CreateRoom(string adminPassword, string roomName, string roomPassword)
    {
        if (adminPassword != _adminPassword)
        {
            _logger.LogWarning("CreateRoom failed: Invalid Admin Password.");
            return;
        }

        var roomId = Slugify(roomName) + "-" + Guid.NewGuid().ToString("n").Substring(0, 4);
        var newRoom = new Room
        {
            Id = roomId,
            Name = roomName,
            Password = roomPassword
        };

        if (Rooms.TryAdd(roomId, newRoom))
        {
            _logger.LogInformation("New room created: {RoomName} ({RoomId}).", roomName, roomId);
            await Clients.All.SendAsync("RoomCreated", new { Id = roomId, Name = roomName });
        }
    }

    public static List<object> GetRooms()
    {
        return Rooms.Values.Select(r => new { r.Id, r.Name, ParticipantCount = r.Participants.Count }).Cast<object>().ToList();
    }

    public async Task SendSignal(string targetConnectionId, string signal)
    {
        var sender = FindParticipant(Context.ConnectionId);
        var receiver = FindParticipant(targetConnectionId);

        if (sender != null && receiver != null && sender.RoomId == receiver.RoomId)
        {
            await Clients.Client(targetConnectionId).SendAsync(SignalREvents.ReceiveSignal, Context.ConnectionId, signal);
        }
    }

    public async Task SendChatMessage(string message)
    {
        var sender = FindParticipant(Context.ConnectionId);
        if (sender != null)
        {
            var sanitizedMessage = System.Net.WebUtility.HtmlEncode(message?.Trim() ?? string.Empty);
            if (string.IsNullOrEmpty(sanitizedMessage)) return;

            var chatMessage = new ChatMessage
            {
                DisplayName = sender.DisplayName,
                Message = sanitizedMessage,
                Timestamp = DateTime.UtcNow
            };

            await _chatHistoryService.WriteMessageAsync(sender.RoomId, chatMessage);

            await Clients.Group(sender.RoomId).SendAsync(SignalREvents.ReceiveChatMessage, chatMessage.DisplayName, chatMessage.Message, chatMessage.Timestamp);
        }
    }

    public async Task UpdateState(string stateType, bool value)
    {
        var participant = FindParticipant(Context.ConnectionId);
        if (participant != null)
        {
            switch (stateType.ToLower())
            {
                case SignalREvents.Muted:
                    participant.IsMuted = value;
                    break;
                case SignalREvents.Deafened:
                    participant.IsDeafened = value;
                    break;
            }
            await Clients.Group(participant.RoomId).SendAsync(SignalREvents.PeerStateUpdated, Context.ConnectionId, stateType, value);
        }
    }

    private Participant? FindParticipant(string connectionId)
    {
        foreach (var room in Rooms.Values)
        {
            if (room.Participants.TryGetValue(connectionId, out var participant))
            {
                return participant;
            }
        }
        return null;
    }

    private static string Slugify(string phrase)
    {
        var str = phrase.ToLower();
        str = System.Text.RegularExpressions.Regex.Replace(str, @"[^a-z0-9\s-]", "");
        str = System.Text.RegularExpressions.Regex.Replace(str, @"\s+", " ").Trim();
        str = System.Text.RegularExpressions.Regex.Replace(str, @"\s", "-");
        return str;
    }
}
