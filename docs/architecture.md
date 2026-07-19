# Architecture

GVoice server is a single ASP.NET Core (`net10.0`) project, `GVoice.API`. Its job
is narrow: broker WebRTC signaling, track room/participant state, and store text
chat. **Audio and video never pass through the server** — clients form a
peer-to-peer WebRTC mesh and the server only relays the SDP/ICE handshake between
them.

## Component map

```
GVoice.API/
├── Program.cs                       # composition root: DI, CORS, REST endpoints, hub mapping
├── Hubs/
│   ├── SignalingHub.cs              # the single SignalR hub (partial class)
│   └── SignalREvents.cs             # const string names for every method/event/state type
├── Services/
│   ├── IRoomService.cs / Implementations/RoomService.cs           # rooms (in-memory)
│   ├── IParticipantService.cs / Implementations/ParticipantService.cs  # participants (in-memory)
│   └── XmlChatHistoryService.cs     # chat history (persisted to XML)
├── Models/                          # Room, Participant, ChatMessage
└── Config/RoomConfig.cs             # binding target for the DefaultRooms section
```

## The SignalR hub is the single realtime entry point

`SignalingHub` (`Hubs/SignalingHub.cs`), mapped at **`/hub/signaling`**, is the
only hub and the entry point for all real-time interaction. Clients invoke hub
methods; the server pushes events back. Every method name, event name, and state
type is centralized as a `const string` in `Hubs/SignalREvents.cs` — always
reference those constants rather than hardcoding strings, and keep the client and
server in sync through that file.

The full contract (methods, events, payloads) is documented in
[signalr-and-rest-api.md](./signalr-and-rest-api.md).

`SignalingHub` is a `partial class`, so additional hub methods may be split
across files under `Hubs/`.

## State is in-memory and singleton-scoped

`RoomService` and `ParticipantService` are registered as **singletons** in
`Program.cs`, and they hold their state in `static readonly ConcurrentDictionary`
fields:

- `RoomService.Rooms` — `ConcurrentDictionary<string /*roomId*/, Room>`
- `ParticipantService.Participants` — `ConcurrentDictionary<string /*connectionId*/, Participant>`
- `Room.Participants` — a nested `ConcurrentDictionary<string /*connectionId*/, Participant>`

Because these fields are `static`, they are effectively process-global (a fresh
service instance still sees the same dictionaries — a detail that matters for
testing; see [testing.md](./testing.md)).

Consequences to keep in mind:

- **State does not survive a restart.** Rooms and participants are rebuilt from
  scratch on every process start. The `DefaultRooms` config section is replayed
  on startup to recreate the seed rooms (`General`, `Gaming`, `Music`). Only chat
  history is persisted (to disk).
- **Participants are tracked in two places** — the room's `Participants` dict and
  `ParticipantService` — and both must be updated together. See `Join` (adds to
  both) and `LeaveCurrentRoomAsync` / `OnDisconnectedAsync` (removes from both).
- **Mutable participant fields** (`IsMuted`, `IsDeafened`, `IsSharingScreen`)
  are guarded by `lock (participant)` inside `ParticipantService`
  so concurrent state updates on one participant don't tear.

Capacity is capped at `MaxUsersPerRoom = 10` (`RoomService`). See the TOCTOU note
in [code-review.md](./code-review.md) about the non-atomic full-check + add.

## Identity keys

- **A participant** is identified by its SignalR `ConnectionId` — per-connection
  and transient. There is no persistent user identity or token.
- **A room** is identified by its `Id`, a **slug** derived from the room name via
  `RoomService.Slugify` (lowercase, spaces to dashes, non-alphanumerics dropped).
  It is not a random GUID, so room names must produce distinct slugs; on
  collision `CreateRoomInternal` appends a numeric suffix (`-2`, `-3`, …) to keep
  ids unique and non-empty.

## Connection lifecycle

Joining and leaving are the two lifecycle events.

**Join** (`SignalingHub.Join(roomId, password, displayName, isListenOnly)`):

1. `ValidateRoomJoin` looks up the room and checks the password and capacity. On
   failure it sends `RoomNotFound`, `InvalidPassword`, or `RoomFull` to the caller
   and stops.
2. If this connection is already a member of some room, `LeaveCurrentRoomAsync`
   is called first so the connection is never left as a ghost in another room /
   SignalR group.
3. `displayName` is sanitized (HTML-encode + trim, capped at 20 chars).
4. The `Participant` is added to the room (`roomService.Join` → `TryAdd`) and to
   `ParticipantService`, then added to the SignalR group named after `roomId`.
5. `SendJoinData` sends the caller its chat history (`ReceiveChatHistory`) and
   room snapshot (`RoomJoined`), and notifies the rest of the group (`PeerJoined`).

**Leave** — there is **no explicit leave method**. Cleanup happens exclusively in
`OnDisconnectedAsync`, which delegates to `LeaveCurrentRoomAsync(connectionId)`:

1. Remove the participant from `ParticipantService` (returns `null` if unknown →
   nothing to do).
2. Remove it from the room's `Participants` dict.
3. Remove the connection from the SignalR group.
4. Broadcast `PeerLeft (connectionId, displayName)` to the remaining group.

`LeaveCurrentRoomAsync` is idempotent-ish and is also reused by `Join` for the
"switch rooms" case above.

## Chat history (`XmlChatHistoryService`)

Chat is the only persisted state. `XmlChatHistoryService` is a singleton that
writes **one XML file per room** under `ChatHistoryPath` (default `chat-history/`;
the directory is created at startup).

- **File naming:** the room id is sanitized against `Path.GetInvalidFileNameChars()`
  (invalid chars replaced with `_`) to form `<roomId>.xml`.
- **Cap:** history is capped at **100 messages**, FIFO — on each write, the oldest
  `Message` elements beyond 100 are removed.
- **Concurrency:** a single process-wide `SemaphoreSlim(1, 1)` (`_fileLock`)
  serializes all reads and writes, so the file is never touched concurrently.
- **Resilience:** on read, a corrupt/unparseable file yields an empty list rather
  than throwing; on write, a corrupt existing file is replaced with a fresh
  document so a one-time corruption can't permanently break a room's chat.
- **Cost:** each write re-serializes the whole document (`File.WriteAllTextAsync`),
  which is O(n) in message count — fine at the 100-message cap and this scale (see
  [code-review.md](./code-review.md)).

XML shape:

```xml
<ChatHistory>
  <Message>
    <DisplayName>Alice</DisplayName>
    <Text>gg</Text>
    <Timestamp>2026-01-02T03:04:05.0000000Z</Timestamp>
  </Message>
  <!-- … up to 100 -->
</ChatHistory>
```

## CORS

CORS is configured in `Program.cs` from the required `Cors:AllowedOrigins`
config array into a policy named `AllowAngular`:

- `WithOrigins(allowedOrigins)` — origins come from config (fails fast if the
  section is missing).
- `AllowAnyHeader()`, `AllowAnyMethod()`.
- `AllowCredentials()` — **required for SignalR** (the WebSocket handshake carries
  credentials).

In production behind Caddy the client is same-origin (`/api/*` on the same host),
so CORS is effectively a non-issue there; the allowed-origins list matters mainly
for local development (`http://localhost:4200`). See
[configuration.md](./configuration.md).

## End-to-end signaling flow

```
  Client A                     GVoice server (SignalingHub)                 Client B
     │                                   │                                     │
     │  Join(room, pw, name, listenOnly) │                                     │
     ├──────────────────────────────────▶│  validate pw + capacity             │
     │                                    │  add to RoomService + ParticipantService
     │  ◀── ReceiveChatHistory([...]) ────┤                                     │
     │  ◀── RoomJoined({name,parts}) ─────┤                                     │
     │                                    │──── PeerJoined(participant) ───────▶│
     │                                    │                                     │
     │  SendSignal(B, sdpOffer)           │  (same-room check)                  │
     ├──────────────────────────────────▶│──── ReceiveSignal(A, sdpOffer) ────▶│
     │                                    │                                     │
     │                                    │  ◀──── SendSignal(A, sdpAnswer) ────┤
     │  ◀── ReceiveSignal(B, sdpAnswer) ──┤                                     │
     │                                    │                                     │
     │◀═══════════ WebRTC P2P media (audio/screen) — never via server ════════▶│
     │                                    │                                     │
     │  UpdateState("muted", true)        │                                     │
     ├──────────────────────────────────▶│──── PeerStateUpdated(A,"muted",T) ─▶│
     │                                    │                                     │
     │  SendChatMessage("gg")             │  sanitize + persist to XML          │
     ├──────────────────────────────────▶│─ ReceiveChatMessage(name,text,ts) ─▶│ (whole group,
     │  ◀── ReceiveChatMessage(...) ──────┤                                     │  incl. sender)
     │                                    │                                     │
     │  (disconnect / tab close)          │  OnDisconnectedAsync                │
     ├──────────────────────────────────▶│  remove from both stores + group    │
     │                                    │──── PeerLeft(connId, name) ────────▶│
```

Key points:

- **Signal relay is targeted and same-room only.** `SendSignal(target, signal)`
  forwards `ReceiveSignal(senderConnId, signal)` to exactly one connection, and
  only if sender and receiver are in the same room.
- **Chat is broadcast to the whole group including the sender** (`Clients.Group`),
  so the sender's own UI can render from the server echo.
- **State/peer events go to the whole group.** `PeerJoined` goes to
  `OthersInGroup`; `RoomJoined` and `ReceiveChatHistory` go only to the caller.

For exact payload shapes, see
[signalr-and-rest-api.md](./signalr-and-rest-api.md). For the deployment topology
(Caddy, `/api` prefix stripping, coturn/TURN), see
[deployment.md](./deployment.md).
