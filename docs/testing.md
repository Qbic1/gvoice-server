# Testing

The solution (`GVoice.slnx`) contains two projects: `GVoice.API` and the xUnit
test project **`GVoice.Tests`**. Tests target the service layer directly (no HTTP
/ SignalR host is spun up).

## Running the tests

```bash
dotnet restore
dotnet build
dotnet test                                   # run the whole suite
dotnet test --filter FullyQualifiedName~RoomServiceTests   # run one class
```

Test packages (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`)
are pinned centrally in `Directory.Packages.props`.

## What's covered

Currently **18 tests** across three classes, exercising the three stateful
services:

| Test class | Under test | Representative cases |
|---|---|---|
| `RoomServiceTests` | `RoomService` | slugification of room names into ids; numeric-suffix on duplicate names; plaintext password check; `Join` add + duplicate-connection rejection; unknown-room rejection; capacity (`IsRoomFull` true only at 10); `GetParticipants` returns display names; `DefaultRooms` seeded from config. |
| `ParticipantServiceTests` | `ParticipantService` | `Get` null for unknown; create/update round-trip; `Remove` returns then nulls (second remove is a no-op); state setters return `false` for unknown connection; state setters mutate a present participant (`IsMuted`/`IsDeafened`/`IsSharingScreen`). |
| `XmlChatHistoryServiceTests` | `XmlChatHistoryService` | write→read round-trip (incl. timestamp); empty history for unknown room; FIFO cap at 100 (writes 105, keeps `m5..m104`); invalid room-id chars don't throw; corrupt file → read returns empty and a subsequent write recovers. |

`XmlChatHistoryServiceTests` writes to a unique temp directory per test class
(`Path.GetTempPath()/gvoice-tests/<guid>`) and deletes it in `Dispose`, so file
I/O is isolated and self-cleaning. `TestConfig.From(...)` builds an in-memory
`IConfiguration` for injecting config into service constructors.

## Caveat: static in-memory state complicates isolation

`RoomService` and `ParticipantService` keep their state in **`static`
`ConcurrentDictionary`** fields (see [architecture.md](./architecture.md)). That
means every `new RoomService(...)` / `new ParticipantService()` in a test shares
the **same process-wide dictionaries** — there is no clean per-test reset, and
xUnit runs test classes in parallel by default.

The suite works around this by never assuming an empty store and by using
**unique names/ids per test** so entries can't collide:

- `TestConfig.UniqueName()` returns `"t" + Guid.ToString("N")` — a slug-safe,
  collision-free base name, so room ids stay predictable despite the shared state.
- `ParticipantServiceTests` keys everything by `Guid.NewGuid().ToString()`
  connection ids.

When adding tests, follow the same discipline: **don't rely on the store being
empty, and generate unique keys.** The cleaner long-term fix is to make the state
instance-scoped (documented as a finding in [code-review.md](./code-review.md)).

## Continuous integration

`.github/workflows/dotnet.yml` runs on push and pull request to `main`:

```
setup-dotnet (10.0.101) → dotnet restore → dotnet build --no-restore → dotnet test --no-build
```

The `test` step now actually executes the `GVoice.Tests` suite (previously there
was no test project and the step was a no-op). Keep CI green — add new tests under
`GVoice.Tests` (or a new test project referenced from `GVoice.slnx`).
