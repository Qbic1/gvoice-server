using GVoice.API.Models;
using GVoice.API.Services;

namespace GVoice.Tests;

public class XmlChatHistoryServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly XmlChatHistoryService _svc;

    public XmlChatHistoryServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "gvoice-tests", Guid.NewGuid().ToString("N"));
        _svc = new XmlChatHistoryService(TestConfig.From(("ChatHistoryPath", _dir)));
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static ChatMessage Msg(string text, string name = "Alice") =>
        new() { DisplayName = name, Message = text, Timestamp = DateTime.UtcNow };

    [Fact]
    public async Task WriteThenRead_RoundTripsMessage()
    {
        var ts = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc);
        await _svc.WriteMessageAsync("room1", new ChatMessage { DisplayName = "Bob", Message = "hi", Timestamp = ts });

        var history = await _svc.ReadHistoryAsync("room1");

        var msg = Assert.Single(history);
        Assert.Equal("Bob", msg.DisplayName);
        Assert.Equal("hi", msg.Message);
        Assert.Equal(ts, msg.Timestamp);
    }

    [Fact]
    public async Task ReadHistory_ForUnknownRoom_ReturnsEmpty()
    {
        var history = await _svc.ReadHistoryAsync("never-written");
        Assert.Empty(history);
    }

    [Fact]
    public async Task History_IsCappedAtHundred_DroppingOldestFirst()
    {
        for (var i = 0; i < 105; i++)
            await _svc.WriteMessageAsync("cap", Msg($"m{i}"));

        var history = await _svc.ReadHistoryAsync("cap");

        Assert.Equal(100, history.Count);
        // Oldest five (m0..m4) dropped; the window is m5..m104.
        Assert.Equal("m5", history.First().Message);
        Assert.Equal("m104", history.Last().Message);
    }

    [Fact]
    public async Task InvalidRoomIdCharacters_DoNotThrow()
    {
        var weird = "a/b:c*?<>|room";
        await _svc.WriteMessageAsync(weird, Msg("ok"));

        var history = await _svc.ReadHistoryAsync(weird);
        Assert.Single(history);
    }

    [Fact]
    public async Task CorruptHistoryFile_ReadReturnsEmpty_AndWriteRecovers()
    {
        // Write a valid message so the file exists, then corrupt it.
        await _svc.WriteMessageAsync("corrupt", Msg("first"));
        var filePath = Path.Combine(_dir, "corrupt.xml");
        await File.WriteAllTextAsync(filePath, "this is not xml <<<");

        // Reading a corrupt file must not throw.
        var afterCorruption = await _svc.ReadHistoryAsync("corrupt");
        Assert.Empty(afterCorruption);

        // Writing recovers by starting a fresh document.
        await _svc.WriteMessageAsync("corrupt", Msg("second"));
        var recovered = await _svc.ReadHistoryAsync("corrupt");
        Assert.Single(recovered);
        Assert.Equal("second", recovered[0].Message);
    }
}
