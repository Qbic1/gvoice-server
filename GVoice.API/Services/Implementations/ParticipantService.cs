using System.Collections.Concurrent;
using GVoice.API.Models;
using GVoice.API.Services;

internal class ParticipantService : IParticipantService
{
    private static readonly ConcurrentDictionary<string, Participant> Participants = new();

    public void CreateOrUpdate(Participant participant)
    {
        Participants[participant.ConnectionId] = participant;
    }

    public Participant? Get(string connectionId)
    {
        Participants.TryGetValue(connectionId, out var participant);
        return participant;
    }

    public Participant? Remove(string connectionId)
    {
        if (Participants.TryRemove(connectionId, out var participant))
        {
            return participant;
        }

        return null;
    }

    public bool SetMuted(string connectionId, bool value)
    {
        var participant = Get(connectionId);
        if (participant is null) return false;

        participant.IsMuted = value;
        return true;
    }

    public bool SetDeafened(string connectionId, bool value)
    {
        var participant = Get(connectionId);
        if (participant is null) return false;

        participant.IsDeafened = value;
        return true;
    }

    public bool SetAudioSettings(string connectionId, AudioSettings settings)
    {
        var participant = Get(connectionId);
        if (participant is null) return false;

        participant.AudioSettings = settings;
        return true;
    }
}