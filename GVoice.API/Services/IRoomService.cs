using GVoice.API.Models;

namespace GVoice.API.Services;

public interface IRoomService
{
    IReadOnlyCollection<Room> Get();
    Room? Get(string roomId);
    bool IsPasswordCorrect(string roomId, string password);
    bool IsRoomFull(string roomId);
    Room Create(string name, string password);
    bool Join(string roomId, Participant participant);
}