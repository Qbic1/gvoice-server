using GVoice.API.Models;
using Microsoft.AspNetCore.SignalR;

namespace GVoice.API.Hubs;

public class SignalingHub : Hub
{
    private static readonly Dictionary<string, Participant> ConnectedParticipants = [];
    private const int MaxUsers = 10;

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        if (ConnectedParticipants.TryGetValue(Context.ConnectionId, out var participant))
        {
            ConnectedParticipants.Remove(Context.ConnectionId);
            await Clients.Others.SendAsync(SignalREvents.PeerLeft, Context.ConnectionId, participant.DisplayName);
        }
        await base.OnDisconnectedAsync(exception);
    }

    public async Task Join(string displayName, bool isListenOnly)
    {
        if (ConnectedParticipants.Count >= MaxUsers)
        {
            await Clients.Caller.SendAsync(SignalREvents.RoomFull);
            return;
        }

        var participant = new Participant
        {
            ConnectionId = Context.ConnectionId,
            DisplayName = displayName,
            IsListenOnly = isListenOnly
        };

        ConnectedParticipants[Context.ConnectionId] = participant;

        // Notify others about the new participant
        await Clients.Others.SendAsync(SignalREvents.PeerJoined, participant);

        // Send the list of ALL participants (including the caller) to the new user
        var allParticipants = ConnectedParticipants.Values.ToList();

        await Clients.Caller.SendAsync(SignalREvents.RoomJoined, allParticipants);
    }

    public async Task SendSignal(string targetConnectionId, string signal)
    {
        if (ConnectedParticipants.ContainsKey(Context.ConnectionId) && ConnectedParticipants.ContainsKey(targetConnectionId))
        {
            await Clients.Client(targetConnectionId).SendAsync(SignalREvents.ReceiveSignal, Context.ConnectionId, signal);
        }
    }

    public async Task SendChatMessage(string message)
    {
        if (ConnectedParticipants.TryGetValue(Context.ConnectionId, out var participant))
        {
            await Clients.All.SendAsync(SignalREvents.ReceiveChatMessage, participant.DisplayName, message, DateTime.UtcNow);
        }
    }

    public async Task UpdateState(string stateType, bool value)
    {
        if (ConnectedParticipants.TryGetValue(Context.ConnectionId, out var participant))
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
            // Broadcast to ALL so the caller also receives the update and refreshes their local UI
            await Clients.All.SendAsync(SignalREvents.PeerStateUpdated, Context.ConnectionId, stateType, value);
        }
    }
}
