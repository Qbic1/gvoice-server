using Microsoft.Extensions.Configuration;

namespace GVoice.Tests;

internal static class TestConfig
{
    public static IConfiguration From(params (string Key, string? Value)[] entries) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(entries.Select(e => new KeyValuePair<string, string?>(e.Key, e.Value)))
            .Build();

    /// <summary>A short, collision-free base name (letters/digits only, lowercase),
    /// so slug ids stay predictable despite RoomService's process-wide static state.</summary>
    public static string UniqueName() => "t" + Guid.NewGuid().ToString("N");
}
