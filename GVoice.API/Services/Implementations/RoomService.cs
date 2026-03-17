using System.Collections.Concurrent;
using GVoice.API.Config;
using GVoice.API.Models;

namespace GVoice.API.Services.Implementations;

internal class RoomService : IRoomService
{
    private const int MaxUsersPerRoom = 10;

    private static readonly ConcurrentDictionary<string, Room> Rooms = new();

    public RoomService(IConfiguration configuration)
    {
        var rooms = configuration
            .GetSection("DefaultRooms")
            .Get<RoomConfig[]>() ?? [];

        foreach(var room in rooms)
        {
            CreateRoomInternal(room.Name, room.Password);
        }
    }

    public Room Create(string name, string password)
    {
        return CreateRoomInternal(name, password);
    }

    public IReadOnlyCollection<Room> Get()
    {
        return Rooms.Values.ToArray().AsReadOnly();
    }

    public Room? Get(string roomId)
    {
        Rooms.TryGetValue(roomId, out var room);
        return room;
    }

    public bool IsPasswordCorrect(string roomId, string password)
    {
        var room = Get(roomId);
        return room?.Password == password;
    }

    public bool IsRoomFull(string roomId)
    {
        var room = Get(roomId);
        return room?.Participants.Count >= MaxUsersPerRoom;
    }

    public bool Join(string roomId, Participant participant)
    {
        var room = Get(roomId);

        if(room is null)
            return false;

        return room.Participants.TryAdd(participant.ConnectionId, participant);
    }

    private static Room CreateRoomInternal(string roomName, string password)
    {
        var postfix = Guid.NewGuid().ToString("n")[..4];
        var roomId = $"{Slugify(roomName)}-{postfix}";

        var room = new Room
        {
            Id = roomId,
            Name = roomName,
            Password = password
        };

        if (!Rooms.TryAdd(roomId, room))
            throw new Exception("Cant create room");

        return room;
    }

    private static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "room";

        return string.Join("-",
            value.ToLowerInvariant()
                 .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                 .Select(x => new string([.. x.Where(char.IsLetterOrDigit)]))
                 .Where(x => x.Length > 0));
    }
}