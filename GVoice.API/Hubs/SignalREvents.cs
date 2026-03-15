namespace GVoice.API.Hubs;

public static class SignalREvents
{
    // Events sent from server to client
    public const string RoomFull = "RoomFull";
    public const string RoomJoined = "RoomJoined";
    public const string PeerJoined = "PeerJoined";
    public const string PeerLeft = "PeerLeft";
    public const string ReceiveSignal = "ReceiveSignal";
    public const string ReceiveChatMessage = "ReceiveChatMessage";
    public const string PeerStateUpdated = "PeerStateUpdated";

    // Events sent from client to server
    public const string Join = "Join";
    public const string SendSignal = "SendSignal";
    public const string SendChatMessage = "SendChatMessage";
    public const string UpdateState = "UpdateState";
    // State Types
    public const string Muted = "muted";
    public const string Deafened = "deafened";
}
