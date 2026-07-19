using GVoice.API.Models;
using GVoice.API.Services.Implementations;

namespace GVoice.Tests;

public class RoomServiceTests
{
    private static RoomService NewService() => new(TestConfig.From());

    [Fact]
    public void Create_SlugifiesRoomName_IntoLowercaseDashedId()
    {
        var svc = NewService();
        // "t<hex>" is already slug-safe; embed spaces/caps to exercise Slugify.
        var name = TestConfig.UniqueName();
        var room = svc.Create($"{name} Room", "pw");

        Assert.Equal($"{name}-room", room.Id);
        Assert.Same(room, svc.Get(room.Id));
    }

    [Fact]
    public void Create_WithDuplicateName_AppendsNumericSuffix()
    {
        var svc = NewService();
        var name = TestConfig.UniqueName();

        var first = svc.Create(name, "pw");
        var second = svc.Create(name, "pw");

        Assert.Equal(name, first.Id);
        Assert.Equal($"{name}-2", second.Id);
    }

    [Fact]
    public void IsPasswordCorrect_ChecksPlaintextPassword()
    {
        var svc = NewService();
        var room = svc.Create(TestConfig.UniqueName(), "secret");

        Assert.True(svc.IsPasswordCorrect(room.Id, "secret"));
        Assert.False(svc.IsPasswordCorrect(room.Id, "wrong"));
    }

    [Fact]
    public void Join_AddsParticipant_AndRejectsDuplicateConnectionId()
    {
        var svc = NewService();
        var room = svc.Create(TestConfig.UniqueName(), "pw");
        var p = new Participant { ConnectionId = "c1", RoomId = room.Id, DisplayName = "Alice" };

        Assert.True(svc.Join(room.Id, p));
        Assert.False(svc.Join(room.Id, p)); // same connection id
        Assert.Single(room.Participants);
    }

    [Fact]
    public void Join_ReturnsFalse_ForUnknownRoom()
    {
        var svc = NewService();
        var p = new Participant { ConnectionId = "c1", RoomId = "nope", DisplayName = "A" };

        Assert.False(svc.Join("does-not-exist", p));
    }

    [Fact]
    public void IsRoomFull_TrueOnlyAtTenParticipants()
    {
        var svc = NewService();
        var room = svc.Create(TestConfig.UniqueName(), "pw");

        for (var i = 0; i < 9; i++)
            svc.Join(room.Id, new Participant { ConnectionId = $"c{i}", RoomId = room.Id, DisplayName = $"U{i}" });

        Assert.False(svc.IsRoomFull(room.Id));

        svc.Join(room.Id, new Participant { ConnectionId = "c9", RoomId = room.Id, DisplayName = "U9" });
        Assert.True(svc.IsRoomFull(room.Id));
    }

    [Fact]
    public void GetParticipants_ReturnsDisplayNames()
    {
        var svc = NewService();
        var room = svc.Create(TestConfig.UniqueName(), "pw");
        svc.Join(room.Id, new Participant { ConnectionId = "c1", RoomId = room.Id, DisplayName = "Alice" });
        svc.Join(room.Id, new Participant { ConnectionId = "c2", RoomId = room.Id, DisplayName = "Bob" });

        var names = svc.GetParticipants(room.Id).ToList();

        Assert.Contains("Alice", names);
        Assert.Contains("Bob", names);
        Assert.Equal(2, names.Count);
    }

    [Fact]
    public void DefaultRooms_AreSeededFromConfiguration()
    {
        var name = TestConfig.UniqueName();
        var config = TestConfig.From(
            ("DefaultRooms:0:Name", name),
            ("DefaultRooms:0:Password", "pw"));

        var svc = new RoomService(config);

        Assert.NotNull(svc.Get(name));
    }
}
