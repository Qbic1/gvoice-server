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
        if (!Participants.TryGetValue(connectionId, out var participant))
            return false;

        lock (participant)
        {
            participant.IsMuted = value;
        }
        return true;
    }

    public bool SetDeafened(string connectionId, bool value)
    {
        if (!Participants.TryGetValue(connectionId, out var participant))
            return false;

        lock (participant)
        {
            participant.IsDeafened = value;
        }
        return true;
    }

    public bool SetSharingScreen(string connectionId, bool value)
    {
        if (!Participants.TryGetValue(connectionId, out var participant))
            return false;

        lock (participant)
        {
            participant.IsSharingScreen = value;
        }
        return true;
    }
}