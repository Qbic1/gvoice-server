# Code review — server

A structured report of correctness bugs, security issues, and optimization
opportunities in the GVoice backend. Each finding carries a **status**
(`FIXED` / `OPEN`), a **severity**, the primary **file(s)**, the issue, and a
recommendation.

Context that shapes severity throughout: the target scale is **peak ≤10
concurrent users across several rooms** on a single dedicated server behind Caddy.
Several theoretical issues are low-impact at this scale and are documented rather
than urgently fixed.

## Severity legend

- **HIGH-SEC** — security-sensitive, high impact.
- **MED** — real issue, bounded impact at current scale.
- **LOW** — minor / cosmetic / latent.
- **OPT** — optimization or engineering-hygiene opportunity.

---

## Findings

### 1. Room password + roster leaked in `RoomCreated` broadcast

- **Status:** FIXED
- **Severity:** HIGH-SEC
- **File:** `GVoice.API/Hubs/SignalingHub.cs` (`CreateRoom`)
- **Issue:** `CreateRoom` broadcast the whole `Room` object to `Clients.All` via
  `RoomCreated`. `Room` carries the **plaintext `Password`** and the participant
  list, so every connected client received each new room's password.
- **Fix applied:** broadcast only a safe projection —
  `await Clients.All.SendAsync(SignalREvents.RoomCreated, new { room.Id, room.Name });`
- **Recommendation / follow-up:** never serialize the `Room` model to clients.
  Treat `{ Id, Name }` as the only client-safe shape (plus a count for the lobby).
  See [security.md](./security.md).

### 2. No length cap on chat messages + 10 MB receive limit

- **Status:** OPEN
- **Severity:** MED
- **Files:** `GVoice.API/Hubs/SignalingHub.cs` (`SendChatMessage`, `Sanitize`),
  `GVoice.API/Program.cs` (`MaximumReceiveMessageSize`),
  `GVoice.API/Services/XmlChatHistoryService.cs`
- **Issue:** `SendChatMessage` sanitizes with the default `maxLength =
  int.MaxValue` (no cap). Combined with `MaximumReceiveMessageSize = 10 MB` and
  clients that embed image **data-URLs** (~6.7 MB after base64), a single message
  can be multi-megabyte. Such messages are written into the room's XML history and
  broadcast to every peer, bloating storage and every recipient's payload.
- **Recommendation:** cap chat **text** length (e.g. a few KB) via `Sanitize`'s
  `maxLength`, and move image sharing to a dedicated upload endpoint (store/serve
  the blob, put a URL in chat) rather than inlining data-URLs into signaling.

### 3. TOCTOU between `IsRoomFull` and `Join`

- **Status:** OPEN
- **Severity:** MED
- **Files:** `GVoice.API/Hubs/SignalingHub.cs` (`ValidateRoomJoin` → `Join`),
  `GVoice.API/Services/Implementations/RoomService.cs`
- **Issue:** capacity is enforced as check-then-act: `IsRoomFull(roomId)` is
  evaluated, then later `RoomService.Join` does `Participants.TryAdd`. Between the
  two, concurrent joins can slip past the check, so a room could briefly exceed
  `MaxUsersPerRoom = 10`.
- **Mitigation in place:** `TryAdd` is atomic (no lost/duplicated entries), and at
  ≤10-user scale simultaneous joins are unlikely.
- **Recommendation:** enforce the cap atomically inside the add (e.g. check count
  under the same critical section, or use a bounded structure) so the limit can't
  be raced.

### 4. Static in-memory state (no persistence, harder tests)

- **Status:** OPEN (by design; documented)
- **Severity:** MED
- **Files:** `GVoice.API/Services/Implementations/RoomService.cs`,
  `GVoice.API/Services/Implementations/ParticipantService.cs`
- **Issue:** room/participant state lives in `static readonly
  ConcurrentDictionary` fields. It does **not** survive a restart (only chat
  history is persisted), and being `static` it is process-global, which
  complicates test isolation (see [testing.md](./testing.md)).
- **Assessment:** acceptable and appropriate at this scale — restarts recreate
  the `DefaultRooms`, and rooms are cheap to rebuild.
- **Recommendation:** keep as-is, but **document the behavior** (done here and in
  [architecture.md](./architecture.md)). If horizontal scaling or restart-survival
  is ever needed, move state to instance scope + an external store (and a SignalR
  backplane).

### 5. Chat history rewrites the whole XML file per message

- **Status:** OPEN
- **Severity:** MED
- **File:** `GVoice.API/Services/XmlChatHistoryService.cs` (`WriteMessageAsync`)
- **Issue:** every message load-parses the document, appends, trims to 100, and
  re-serializes with `File.WriteAllTextAsync` — O(n) in message count per write,
  under a single global `SemaphoreSlim`.
- **Assessment:** fine at 100-message cap and ≤10 users; the shared lock also
  serializes all rooms' writes, but throughput needs are tiny.
- **Recommendation:** acceptable for now; if chat volume grows, switch to an
  append-friendly store (per-room append log, SQLite, etc.).

### 6. `AudioSettings` field mismatch + no listener (dead feature)

- **Status:** FIXED (removed)
- **Severity:** LOW
- **Files:** `GVoice.API/Models/Participant.cs` (`AudioSettings`),
  `GVoice.API/Hubs/SignalingHub.cs` (`UpdateAudioSettings`)
- **Issue:** the server model expected `EnableEnhancement` / `GateSensitivity`,
  but clients sent `enableAudioEnhancements` / `noiseGateThreshold`, so inbound
  values deserialized to defaults. Additionally, **no client listened** for the
  `AudioSettingsUpdated` broadcast — the whole round-trip was inert.
- **Resolution:** the dead feature was removed end-to-end — the
  `UpdateAudioSettings` hub method, the `AudioSettingsUpdated` / `UpdateAudioSettings`
  / `AudioSettings` event constants, `IParticipantService.SetAudioSettings` and its
  implementation, and the `Participant.AudioSettings` property + `AudioSettings`
  model. Local audio processing (client-side `SettingsService`) is unaffected.

### 7. Divergent admin-password defaults

- **Status:** OPEN
- **Severity:** LOW
- **Files:** `GVoice.API/Hubs/SignalingHub.cs`
  (`configuration["AdminPassword"] ?? "default-secret"`),
  `GVoice.API/appsettings.json` (`"AdminPassword": "change-me-123"`)
- **Issue:** two different fallback defaults. Because `appsettings.json` always
  supplies the key, the effective default is `change-me-123`; the hub's
  `default-secret` only surfaces if the key is removed — a latent trap.
- **Recommendation:** unify on a single default and **require an explicit override
  in production** (ideally fail fast if left at a known default). See
  [configuration.md](./configuration.md) and [security.md](./security.md).

### 8. `File.WriteAllText` is not atomic

- **Status:** OPEN
- **Severity:** LOW
- **File:** `GVoice.API/Services/XmlChatHistoryService.cs` (`WriteMessageAsync`)
- **Issue:** a crash mid-write can leave a truncated/corrupt XML file. The impact
  is contained: reads catch parse errors and return an empty list, and the next
  write starts a fresh document — so a corruption costs (at most) that room's
  history, not a crash.
- **Recommendation:** write to a temp file and atomically move/replace to make
  writes crash-safe if history durability matters more.

### 9. Engineering-hygiene optimizations

- **Status:** OPEN
- **Severity:** OPT
- **Suggestions:**
  - **Strongly-typed options:** bind config via `IOptions<T>` (e.g. an
    `AdminOptions`, `ChatHistoryOptions`) instead of indexed `configuration["..."]`
    lookups, for validation and testability.
  - **Rate-limiting:** throttle `SendSignal` and `SendChatMessage` per connection.
  - **Structured logging:** the hub already logs some events; extend consistent
    structured logging (and add logging where `XmlChatHistoryService` currently
    swallows exceptions silently).

---

## Summary

| # | Finding | Severity | Status |
|---|---|---|---|
| 1 | `RoomCreated` leaked room password/roster | HIGH-SEC | FIXED |
| 2 | No chat-message length cap + 10 MB receive limit | MED | OPEN |
| 3 | TOCTOU on room capacity | MED | OPEN |
| 4 | Static in-memory state (no persistence, harder tests) | MED | OPEN (by design) |
| 5 | Full XML rewrite per chat message | MED | OPEN |
| 6 | `AudioSettings` mismatch + no listener (dead feature) | LOW | FIXED (removed) |
| 7 | Divergent admin-password defaults | LOW | OPEN |
| 8 | `File.WriteAllText` not atomic | LOW | OPEN |
| 9 | Options binding / rate-limiting / structured logging | OPT | OPEN |
