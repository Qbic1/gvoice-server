namespace GVoice.API.Models;

public class Participant
{
    public string ConnectionId { get; set; } = string.Empty;
    public string RoomId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public bool IsMuted { get; set; }
    public bool IsDeafened { get; set; }
    public bool IsListenOnly { get; set; }
    public bool IsSharingScreen { get; set; }
}
