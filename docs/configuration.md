# Configuration

All configuration is read through the standard ASP.NET Core
`IConfiguration` stack. Sources, in increasing precedence:

1. `GVoice.API/appsettings.json` — committed defaults.
2. `GVoice.API/appsettings.Development.json` — applied only when
   `ASPNETCORE_ENVIRONMENT=Development`.
3. **Environment variables** — override any key. This is the intended mechanism
   for production and for all secrets.

> **Secrets belong in environment variables on the server, not in committed
> files.** `appsettings.json` ships a placeholder admin password and toy room
> passwords for local convenience; a real deployment must override them (the
> production stack injects `AdminPassword` from `.env` — see
> [deployment.md](./deployment.md)). See [security.md](./security.md) for the
> trust model.

## Overriding with environment variables

ASP.NET Core maps nested config keys to env vars by replacing the `:` separator
with `__` (double underscore). Examples:

```bash
# scalar keys
export AdminPassword='a-long-random-secret'
export ChatHistoryPath='/data/chat-history'

# nested key: Cors:AllowedOrigins[0]
export Cors__AllowedOrigins__0='https://voice-room.example'

# nested key: DefaultRooms[0]
export DefaultRooms__0__Name='General'
export DefaultRooms__0__Password='some-pw'

# host binding
export ASPNETCORE_URLS='http://0.0.0.0:5293'
```

## Keys

| Key | Type | Default (`appsettings.json`) | Purpose | Override via |
|---|---|---|---|---|
| `AdminPassword` | string | `change-me-123` | Shared secret gating `CreateRoom` (hub) and `POST /admin/verify` (REST). **Must be overridden in production.** See the fallback note below. | `AdminPassword` |
| `Cors:AllowedOrigins` | string[] | `["http://localhost:4200"]` | Origins allowed by the `AllowAngular` CORS policy. Credentials are allowed (required for SignalR). **Required section** — startup fails if missing. | `Cors__AllowedOrigins__0`, `__1`, … |
| `ChatHistoryPath` | string | `chat-history` | Directory for per-room XML chat files. Created at startup. Relative paths are inside the working dir (ephemeral in a container — mount a volume). | `ChatHistoryPath` |
| `DefaultRooms` | object[] `{Name, Password}` | `General`, `Gaming`, `Music` (all password `123`) | Rooms seeded into memory on every startup (state is not persisted). Each entry binds to `Config/RoomConfig` (`Name`/`Password` default to `"default"`). | `DefaultRooms__0__Name`, `DefaultRooms__0__Password`, … |
| `Logging:LogLevel:Default` | string | `Information` | Default log level. | `Logging__LogLevel__Default` |
| `Logging:LogLevel:Microsoft.AspNetCore` | string | `Warning` | Framework log level. | `Logging__LogLevel__Microsoft.AspNetCore` |
| `AllowedHosts` | string | `*` (Dev: `localhost`) | Standard ASP.NET Core host filtering. | `AllowedHosts` |
| `ASPNETCORE_URLS` | string (env only) | — (Dev launch profile: `http://localhost:5293`) | Kestrel bind address. The Docker image sets `http://0.0.0.0:5293`. | env var |
| `ASPNETCORE_ENVIRONMENT` | string (env only) | `Development` (launch profile); `Production` (Docker image) | Selects environment; gates Swagger UI (`/openapi`, Development only). | env var |

## Admin password fallback (important)

There are **two** different defaults for the admin password, and they disagree:

- `appsettings.json` sets `AdminPassword = "change-me-123"`.
- `SignalingHub` reads `configuration["AdminPassword"] ?? "default-secret"` — the
  `?? "default-secret"` fallback only takes effect if the key is entirely absent.

Because `appsettings.json` always provides the key, the effective default in a
normal run is `change-me-123`, not `default-secret`. The mismatch is still a
latent trap (remove the appsettings key and the hub silently accepts
`default-secret`). Always set `AdminPassword` explicitly in production so neither
default is ever in play. Tracked as a finding in
[code-review.md](./code-review.md) and [security.md](./security.md).

## Hard-coded, non-configurable values

A few operational limits are set in code, not config:

| Value | Where | Meaning |
|---|---|---|
| `MaximumReceiveMessageSize = 10 * 1024 * 1024` (10 MB) | `Program.cs` (`AddSignalR`) | Max size of a single inbound SignalR message. Chat can carry image data-URLs, so this is deliberately large. Interacts with the missing chat-message length cap — see [code-review.md](./code-review.md). |
| `MaxUsersPerRoom = 10` | `RoomService` | Per-room capacity. |
| `MaxHistoryCount = 100` | `XmlChatHistoryService` | Per-room chat history cap (FIFO). |
| `Sanitize` display-name cap = 20 chars | `SignalingHub` | Display names are truncated to 20 characters. |

To change any of these you must edit the source and rebuild.
