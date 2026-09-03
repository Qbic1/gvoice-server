using GVoice.API.Models;

namespace GVoice.API.Services;

public interface IParticipantService
{
    Participant? Get(string connectionId);
    void CreateOrUpdate(Participant participant);
    Participant? Remove(string connectionId);
    bool SetMuted(string connectionId, bool value);
    bool SetDeafened(string connectionId, bool value);
    bool SetSharingScreen(string connectionId, bool value);
    bool SetAvatar(string connectionId, string value);
}