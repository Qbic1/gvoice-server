namespace GVoice.API.Models;

public class ChatMessage
{
    public required string DisplayName { get; set; }
    public required string Message { get; set; }
    public required DateTime Timestamp { get; set; }
}
