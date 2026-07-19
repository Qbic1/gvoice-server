# GVoice Server — Documentation

GVoice is the backend for a fast, simple group voice chat for gaming. It is a
single ASP.NET Core (`net10.0`) web project that brokers **WebRTC signaling over
SignalR** — it does **not** relay audio/video media itself. Clients establish
peer-to-peer WebRTC connections; the server only exchanges SDP/ICE signals,
tracks room/participant state in memory, and persists text chat history to disk
as XML.

At a glance:

- **One SignalR hub** (`/hub/signaling`) is the single entry point for all
  real-time interaction (join, signal exchange, chat, state updates).
- **State is in-memory and singleton-scoped** (`static ConcurrentDictionary` in
  `RoomService` / `ParticipantService`). It does not survive a restart — only
  chat history is persisted.
- **A tiny REST surface** (`GET /rooms`, `GET /rooms/{roomId}/participants`,
  `POST /admin/verify`) complements the hub.
- **Minimal auth:** a shared admin password gates room creation; each room has a
  plaintext password checked on join. There is no per-user identity/token system.

Target scale is small and deliberate: **peak ≤10 concurrent users across several
rooms**, deployed on a single dedicated server behind Caddy (the client talks to
the backend same-origin under `/api`, which Caddy strips before proxying to the
hub and REST endpoints).

## Table of contents

| Document | What it covers |
|---|---|
| [architecture.md](./architecture.md) | System overview: the signaling hub, in-memory singleton state, identity keys, connection lifecycle, chat history, CORS, and the end-to-end signaling flow (with an ASCII diagram). |
| [configuration.md](./configuration.md) | Every configuration key, its default, and how to override it via environment variables. |
| [signalr-and-rest-api.md](./signalr-and-rest-api.md) | The full, current contract: hub methods (client→server), events (server→client) with payloads, state types, the REST endpoints, and the data models. |
| [security.md](./security.md) | The access/trust model, input sanitization, a recently fixed password-leak, and hardening recommendations. |
| [testing.md](./testing.md) | How to run the test suite, what `GVoice.Tests` covers, the static-state testing caveat, and CI. |
| [code-review.md](./code-review.md) | A structured report of server-side bugs and optimization opportunities, each tagged with status, severity, file, and recommendation. |
| [deployment.md](./deployment.md) | Production deployment on a dedicated server behind Caddy + coturn (topology, secrets, first deploy, redeploy, verification). Templates live in [`deploy/`](./deploy/). |

## Quick start (local)

```bash
dotnet restore                    # restore packages (central versions in Directory.Packages.props)
dotnet build                      # build the solution (GVoice.slnx)
dotnet run --project GVoice.API   # runs at http://localhost:5293 (Development)
dotnet test                       # run the GVoice.Tests suite
```

In Development, Swagger UI is served at `/openapi`. See
[configuration.md](./configuration.md) for local overrides and
[deployment.md](./deployment.md) for the production setup.
