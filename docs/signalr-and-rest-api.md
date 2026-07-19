# SignalR & REST API

This is the complete, current contract between clients and the GVoice server.
Two surfaces exist:

- The **SignalR hub** at `/hub/signaling` — all real-time interaction.
- A **minimal REST API** — lobby listing and admin verification.

Behind Caddy in production the client reaches both under the `/api` prefix
(`/api/hub/signaling`, `/api/rooms`, …); Caddy strips `/api` before proxying. See
[deployment.md](./deployment.md).

All method, event, and state-type names are defined as `const string`s in
`Hubs/SignalREvents.cs`. The tables below use the wire names (the string values).

---

## Hub methods (client → server)

Invoked via the SignalR connection to `/hub/signaling`.

### `Join(string roomId, string password, string displayName, bool isListenOnly)`

Join a room. Validates existence, password, and capacity (max 10). If this
connection was already in a room, it is removed from it first. `displayName` is
sanitized and capped at 20 characters. `isListenOnly` marks a participant who
receives audio but does not publish.

- **On success:** the caller receives `ReceiveChatHistory` then `RoomJoined`;
  other members receive `PeerJoined`.
- **On failure:** the caller receives exactly one of `RoomNotFound`,
  `InvalidPassword`, `RoomFull`, or `JoinFailed` (the last if the underlying add
  fails, e.g. a duplicate connection id).

### `SendSignal(string targetConnectionId, string signal)`

Relay a WebRTC signaling payload (SDP offer/answer or ICE candidate — opaque to
the server) to a single peer. Delivered only if the sender and target are both
known participants **in the same room**; otherwise silently dropped. The target
receives `ReceiveSignal(senderConnectionId, signal)`.

### `SendChatMessage(string message)`

Send a chat message to the sender's room. The message is sanitized (HTML-encode +
trim); empty results are dropped. The message is persisted to the room's XML
history (FIFO cap 100) and broadcast to the **whole group including the sender**
as `ReceiveChatMessage(displayName, message, timestamp)`.

> Note: there is currently **no length cap** on chat text, and
> `MaximumReceiveMessageSize` is 10 MB, so large (e.g. image data-URL) messages
> are accepted, stored, and broadcast. See [code-review.md](./code-review.md).

### `UpdateState(string stateType, bool value)`

Update a boolean presence flag on the caller and broadcast it. `stateType` must
be one of the state-type constants below; anything else is ignored. On success,
the group receives `PeerStateUpdated(connectionId, stateType, value)`.

| `stateType` (wire value) | Participant field |
|---|---|
| `muted` | `IsMuted` |
| `deafened` | `IsDeafened` |
| `sharingScreen` | `IsSharingScreen` |

### `CreateRoom(string adminPassword, string roomName, string roomPassword)`

Create a new room. Requires `adminPassword` to match the server `AdminPassword`
(config); on mismatch the call is logged and ignored (no error is sent back). On
success the room is created (name slugified to an id) and **all connected
clients** receive `RoomCreated`.

> Security: `RoomCreated` deliberately carries **only** `{ Id, Name }` — never the
> room's plaintext password or participant list. This was a fixed leak; see
> [security.md](./security.md) and [code-review.md](./code-review.md).

---

## Events (server → client)

Register handlers for these on the client connection.

| Event | Payload | Sent to | Meaning |
|---|---|---|---|
| `ReceiveChatHistory` | `List<ChatMessage>` | caller | Room chat history, sent right after a successful `Join`. |
| `RoomJoined` | `{ Name: string, Participants: Participant[] }` | caller | Snapshot of the joined room (its display name + current participants, including the caller). |
| `PeerJoined` | `Participant` | others in group | A new participant joined. |
| `PeerLeft` | `(connectionId: string, displayName: string)` | group | A participant disconnected/left. |
| `ReceiveSignal` | `(senderConnectionId: string, signal: string)` | one target | A relayed WebRTC signaling payload from a peer. |
| `PeerStateUpdated` | `(connectionId: string, stateType: string, value: bool)` | group | A peer toggled `muted`/`deafened`/`sharingScreen`. |
| `ReceiveChatMessage` | `(displayName: string, message: string, timestamp: DateTime)` | group (incl. sender) | A new chat message. |
| `RoomCreated` | `{ Id: string, Name: string }` | all clients | A room was created (lobby update). Password/participants intentionally omitted. |
| `RoomNotFound` | — | caller | `Join` failed: no such room. |
| `InvalidPassword` | — | caller | `Join` failed: wrong room password. |
| `RoomFull` | — | caller | `Join` failed: room at capacity (10). |
| `JoinFailed` | — | caller | `Join` failed: could not add participant (e.g. duplicate connection). |

### State types

Used in `UpdateState` and echoed in `PeerStateUpdated`:

| Wire value | Constant |
|---|---|
| `muted` | `SignalREvents.Muted` |
| `deafened` | `SignalREvents.Deafened` |
| `sharingScreen` | `SignalREvents.SharingScreen` |

---

## REST API

Minimal APIs defined in `Program.cs`. No authentication except where noted.

### `GET /rooms`

List all rooms for the lobby. Passwords are **not** included.

**200 OK** — array of:

```json
[
  { "id": "general", "name": "General", "participantCount": 0 },
  { "id": "gaming",  "name": "Gaming",  "participantCount": 2 }
]
```

### `GET /rooms/{roomId}/participants`

List the display names of participants in a room. Returns an empty array for an
unknown room.

**200 OK**:

```json
["Alice", "Bob"]
```

### `POST /admin/verify`

Verify the admin password (used by the client before showing admin UI).

**Request body:**

```json
{ "password": "your-admin-password" }
```

- **200 OK** — password matches `AdminPassword`.
- **401 Unauthorized** — mismatch.

---

## Data models

### `Participant` (`Models/Participant.cs`)

```csharp
public class Participant
{
    public string ConnectionId { get; set; }      // SignalR connection id (identity)
    public string RoomId { get; set; }            // room slug id
    public string DisplayName { get; set; }        // sanitized, ≤20 chars
    public bool   IsMuted { get; set; }
    public bool   IsDeafened { get; set; }
    public bool   IsListenOnly { get; set; }       // set at Join, receive-only
    public bool   IsSharingScreen { get; set; }
}
```

> When serialized to clients (`PeerJoined`, `RoomJoined.Participants`), the full
> `Participant` is sent — including `ConnectionId` and `RoomId`. There is no
> separate DTO.

### `ChatMessage` (`Models/ChatMessage.cs`)

```csharp
public class ChatMessage
{
    public required string   DisplayName { get; set; }
    public required string   Message { get; set; }     // serialized as <Text> in XML
    public required DateTime Timestamp { get; set; }    // UTC, round-trip ("o") format
}
```

### `Room` (`Models/Room.cs`) — server-internal

```csharp
public class Room
{
    public required string Id { get; set; }        // slug derived from Name
    public required string Name { get; set; }
    public required string Password { get; set; }   // plaintext — NEVER sent to clients
    public ConcurrentDictionary<string, Participant> Participants { get; set; } = new();
}
```

The full `Room` object (with `Password` and participants) must never be broadcast.
Client-facing surfaces expose only `{ Id, Name }` (+ a participant count for the
lobby, or the participant list for the caller's own `RoomJoined`).
