using GVoice.API.Models;

// ParticipantService lives in the global namespace (no namespace declaration in source).
public class ParticipantServiceTests
{
    private static Participant NewParticipant(string id) =>
        new() { ConnectionId = id, RoomId = "r", DisplayName = "U" };

    [Fact]
    public void Get_ReturnsNull_ForUnknownConnection()
    {
        var svc = new ParticipantService();
        Assert.Null(svc.Get(Guid.NewGuid().ToString()));
    }

    [Fact]
    public void CreateOrUpdate_ThenGet_ReturnsParticipant()
    {
        var svc = new ParticipantService();
        var id = Guid.NewGuid().ToString();
        var p = NewParticipant(id);

        svc.CreateOrUpdate(p);

        Assert.Same(p, svc.Get(id));
    }

    [Fact]
    public void Remove_ReturnsParticipant_ThenGetIsNull()
    {
        var svc = new ParticipantService();
        var id = Guid.NewGuid().ToString();
        svc.CreateOrUpdate(NewParticipant(id));

        var removed = svc.Remove(id);

        Assert.NotNull(removed);
        Assert.Null(svc.Get(id));
        Assert.Null(svc.Remove(id)); // second remove is a no-op
    }

    [Fact]
    public void StateSetters_ReturnFalse_ForUnknownConnection()
    {
        var svc = new ParticipantService();
        var id = Guid.NewGuid().ToString();

        Assert.False(svc.SetMuted(id, true));
        Assert.False(svc.SetDeafened(id, true));
        Assert.False(svc.SetSharingScreen(id, true));
    }

    [Fact]
    public void StateSetters_MutateParticipant_WhenPresent()
    {
        var svc = new ParticipantService();
        var id = Guid.NewGuid().ToString();
        var p = NewParticipant(id);
        svc.CreateOrUpdate(p);

        Assert.True(svc.SetMuted(id, true));
        Assert.True(svc.SetDeafened(id, true));
        Assert.True(svc.SetSharingScreen(id, true));

        Assert.True(p.IsMuted);
        Assert.True(p.IsDeafened);
        Assert.True(p.IsSharingScreen);
    }
}
