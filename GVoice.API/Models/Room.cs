using System.Collections.Concurrent;

namespace GVoice.API.Models;

public class Room
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public required string Password { get; set; }
    public ConcurrentDictionary<string, Participant> Participants { get; set; } = new();
}
