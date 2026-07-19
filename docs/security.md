# Security

GVoice's security model is intentionally minimal and matches its scope: a
small, trusted group of gamers (**peak ≤10 concurrent users**) on a single
dedicated server behind Caddy/TLS. This document states what protections exist,
what they do **not** cover, and how to harden if the threat model grows.

## Access / trust model

There is **no user identity or token system**. Two shared secrets gate the only
privileged operations:

| Boundary | Mechanism | Where checked |
|---|---|---|
| Create a room | Shared `AdminPassword` (config) | `SignalingHub.CreateRoom`, `POST /admin/verify` |
| Join a room | Per-room **plaintext** `Password` | `SignalingHub.Join` → `RoomService.IsPasswordCorrect` |
| Read lobby / participants | none | `GET /rooms`, `GET /rooms/{roomId}/participants` |

Implications:

- **A participant is only ever a transient SignalR `ConnectionId`.** There is no
  account, no session token, no impersonation protection beyond holding the
  connection. Anyone with the room password can join under any (sanitized) display
  name.
- **Room passwords are stored and compared in plaintext**, in memory (they never
  reach a client — see below). The comparison is a plain `==` (not constant-time),
  but at this scale/threat model that is acceptable.
- **The lobby endpoints are unauthenticated.** `GET /rooms` and
  `GET /rooms/{roomId}/participants` expose room ids/names and participant display
  names to anyone who can reach the server. They deliberately do **not** expose
  passwords.
- **Transport security is the deployment's responsibility.** The app speaks plain
  HTTP; TLS terminates at Caddy. `getUserMedia` and WebSockets require a secure
  context, so HTTPS is mandatory in production — see [deployment.md](./deployment.md).
- **CORS** is restricted to `Cors:AllowedOrigins` with credentials allowed
  (required for SignalR). In production the client is same-origin under `/api`, so
  CORS mainly matters for local dev. See [configuration.md](./configuration.md).

## Input sanitization

Every client-supplied string that is stored or broadcast passes through
`SignalingHub.Sanitize`, which **HTML-encodes** (`WebUtility.HtmlEncode`),
**trims**, and optionally **length-caps**:

```csharp
private static string Sanitize(string? input, int maxLength = int.MaxValue)
{
    var value = System.Net.WebUtility.HtmlEncode(input?.Trim() ?? "");
    return value.Length > maxLength ? value[..maxLength] : value;
}
```

- **Display names** are sanitized and capped at **20 chars** on `Join`.
- **Chat messages** are sanitized on `SendChatMessage`; empty results are dropped.
  There is **no length cap** (`maxLength` defaults to `int.MaxValue`) — see the
  finding below.

HTML-encoding is the primary defense against stored/reflected XSS in the chat and
participant lists rendered by the web client. Follow this pattern for any new
user-supplied input that is stored or echoed.

`XmlChatHistoryService` separately sanitizes the room id against
`Path.GetInvalidFileNameChars()` before using it as a filename, preventing path
traversal / invalid-filename issues from crafted room ids.

## Recently fixed: room password leak in `RoomCreated`

**Fixed.** `CreateRoom` previously broadcast the entire `Room` object to
`Clients.All`, which included the room's **plaintext `Password`** (and its
participant list). Any connected client received every new room's password. The
hub now sends only the non-sensitive projection:

```csharp
await Clients.All.SendAsync(SignalREvents.RoomCreated, new { room.Id, room.Name });
```

General rule reinforced by this fix: **never serialize the `Room` model to
clients.** Expose only `{ Id, Name }` (plus a count for the lobby, or the
participant list for the caller's own `RoomJoined`). See
[signalr-and-rest-api.md](./signalr-and-rest-api.md) and
[code-review.md](./code-review.md) (`HIGH-SEC`, FIXED).

## Hardening recommendations

Prioritized, none blocking at current scale:

1. **Cap chat message length** (and/or move image uploads to a dedicated
   endpoint). With no length cap and a 10 MB `MaximumReceiveMessageSize`, a client
   can persist and broadcast multi-megabyte messages (e.g. image data-URLs),
   bloating the XML history and every peer's payload. Add a text length cap and
   handle images out-of-band.
2. **Rate-limit `SendSignal` and `SendChatMessage`** per connection to blunt spam
   / flood abuse.
3. **Make capacity enforcement atomic.** `IsRoomFull` then `Join` (`TryAdd`) is a
   check-then-act race (TOCTOU); under concurrent joins a room could momentarily
   exceed 10. Enforce the cap inside the atomic add.
4. **Move secrets entirely out of committed files.** `appsettings.json` ships
   `change-me-123` and toy room passwords; production must override via env. Fail
   fast if the admin password is left at a known default.
5. **Unify the admin-password default.** The hub falls back to `default-secret`
   while `appsettings.json` uses `change-me-123`; require an explicit override and
   remove the divergent defaults.
6. **Hash room passwords** (and use constant-time comparison) if room passwords
   ever become sensitive, rather than storing/comparing plaintext.
7. **Consider real identity/tokens** if the trust boundary widens beyond a small
   known group.

Full engineering detail and status for each item is in
[code-review.md](./code-review.md).
