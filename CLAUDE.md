# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

GVoice is the backend for a group voice chat app for gaming. It is a single ASP.NET Core (net10.0) web project that brokers WebRTC signaling over SignalR — it does **not** relay audio/video media itself. Clients establish peer-to-peer WebRTC connections; the server only exchanges SDP/ICE signals, tracks room/participant state, and stores text chat history.

## Commands

```bash
dotnet restore                 # restore packages (central versions in Directory.Packages.props)
dotnet build                   # build the solution (GVoice.slnx)
dotnet run --project GVoice.API   # run locally; http://localhost:5293 (Development)
dotnet test                    # run tests — NOTE: no test project exists yet
```

- The app runs at `http://localhost:5293` (Development). Swagger UI is served at `/openapi` only in Development.
- Docker: `docker build -t gvoice .` then `docker run -p 5293:5293 gvoice` (runs in Production, binds `0.0.0.0:5293`).
- CI (`.github/workflows/dotnet.yml`) runs restore → build → test on push/PR to `main`. `dotnet test` currently finds no tests but is expected to remain green; add tests under a new project if introducing them.

## Architecture

**Signaling flow.** `SignalingHub` (`Hubs/SignalingHub.cs`, mapped at `/hub/signaling`) is the single SignalR hub and the entry point for all real-time interaction. Clients call hub methods (`Join`, `SendSignal`, `SendChatMessage`, `UpdateState`, `UpdateAudioSettings`, `CreateRoom`); the server pushes events back. All SignalR method/event names are centralized as `const string`s in `Hubs/SignalREvents.cs` — **always reference these constants**, never hardcode string names, and keep client/server names in sync via this file.

**State is in-memory and singleton-scoped.** `RoomService` and `ParticipantService` (registered as singletons in `Program.cs`) hold all state in `static readonly ConcurrentDictionary` fields. Consequences to keep in mind:
- Room/participant state does **not** survive restarts (only chat history is persisted to disk). Default rooms are recreated from the `DefaultRooms` config section on startup.
- `Room.Participants` is itself a `ConcurrentDictionary` keyed by `ConnectionId`. Participants are tracked in **two** places — the room's `Participants` dict and `ParticipantService` — and both must be updated together (see `Join` and `OnDisconnectedAsync`).
- Mutations to a `Participant`'s mutable fields (muted/deafened/etc.) are guarded by `lock (participant)` in `ParticipantService`.

**Identity keys.** A participant is identified by SignalR `ConnectionId` (per-connection, transient). A room is identified by `Id`, which is a slug derived from the room name (`RoomService.Slugify`) — not a random GUID, so room names must produce unique slugs.

**Lifecycle.** `OnDisconnectedAsync` is the sole cleanup path: it removes the participant from both stores, removes them from the SignalR group, and broadcasts `PeerLeft`. There is no explicit "leave" method.

**Chat history** is the only persisted state. `XmlChatHistoryService` (singleton) writes one XML file per room under `ChatHistoryPath` (default `chat-history/`), capped at 100 messages (FIFO), serialized with a single process-wide `SemaphoreSlim`. Filenames are sanitized from room id.

**Auth model** is intentionally minimal: a shared `AdminPassword` (config) gates `CreateRoom` and `POST /admin/verify`; each room has a plaintext `Password` checked on `Join`. There is no user identity/token system.

**REST surface** (minimal APIs in `Program.cs`): `GET /rooms`, `GET /rooms/{roomId}/participants`, `POST /admin/verify`. Everything else goes through the hub.

## Conventions

- **Central package management**: all NuGet versions live in `Directory.Packages.props`. Reference packages in `.csproj` **without** a `Version` attribute; add/bump versions in the props file.
- Services follow an interface + `Services/Implementations/` pattern; register new services in `Program.cs`. `IParticipantService`/`IRoomService` are public interfaces with `internal` implementations.
- `SignalingHub` is a `partial class` — additional hub methods may be split across files in `Hubs/`.
- All client-supplied strings that are stored or broadcast are passed through `SignalingHub.Sanitize` (HTML-encode + trim + optional length cap). Follow this for any new user input.
- CORS is locked to `Cors:AllowedOrigins` (config) with credentials allowed (required for SignalR); update this config when adding client origins.
- `Nullable` and `ImplicitUsings` are enabled.
