# Deployment guide (dedicated server)

This is the single source of truth for deploying the whole GVoice stack — backend
(`gvoice-server`), web client (`gvoice-client`) and TURN — on one dedicated
server. The Windows desktop app (`gvoice-wpf`) is distributed separately; see that
repo's `docs/build-and-installer.md`.

Target scale: **peak ≤10 concurrent users across several rooms**, most from the
desktop (WebView2) shell, some from browsers. At this scale a single small VDS
(2 vCPU / 2–4 GB RAM) plus a TURN server is plenty — the server only brokers
signaling, media flows peer-to-peer (relayed through TURN when NAT requires it).

## 1. Topology

```
                    ┌──────────────── VDS ────────────────┐
Browser / WebView2  │   Caddy (:443)                       │
   │  HTTPS + WSS   │    ├── /api/*  → strip /api → api:5293 (SignalR + REST)
   ├───────────────▶    └── /*      → static Angular (/srv/www)             │
   │                │   ASP.NET Core hub (in-memory room/participant state)  │
   │  WebRTC P2P    │   coturn (:3478)  ── TURN relay when P2P can't connect │
   └──────media─────┼──────────────────────────────────────┘
        (direct peer-to-peer, or relayed via TURN)
```

- **Media never touches the ASP.NET server.** It only exchanges SDP/ICE over
  SignalR and holds room state in memory.
- **TLS terminates at Caddy** with operator-provided certificates. `getUserMedia`
  and WebSocket both require a secure context — HTTPS is mandatory, not optional.

## 2. Deploy-root layout

Everything is orchestrated from one directory (the "deploy root"). The two repos
are checked out as siblings; the client is built inside the Caddy image.

```
deploy-root/
├── docker-compose.yml   # from docs/deploy/docker-compose.yml
├── Dockerfile           # from docs/deploy/Dockerfile — builds client + Caddy (context .)
├── Caddyfile            # from docs/deploy/Caddyfile
├── .env                 # from docs/deploy/.env.example  (NOT committed)
├── certs/
│   ├── certificate_fullchain.crt
│   └── certificate.key
├── client/              # git clone of gvoice-client
└── server/              # git clone of gvoice-server  (has its own Dockerfile)
```

Ready-to-copy templates for all of the above live in [`docs/deploy/`](./deploy/).

## 3. Credentials — where each one goes

Two secrets, two very different mechanisms — this trips people up:

| Secret | Set where | When it applies | Notes |
|---|---|---|---|
| **Admin password** | `GVOICE_ADMIN_PASSWORD` in `.env` → `AdminPassword` env on the `api` container | **runtime** | Gates room creation and `POST /api/admin/verify`. Overrides the `appsettings.json` default `change-me-123`. |
| **TURN credential** | `client/src/environments/environment.ts` (`iceServers[].credential`) | **build time** | Compiled into the JS bundle by `ng build`. Must equal coturn's `user=` secret. |

> ⚠️ **The TURN credential is baked into the client bundle at build time**, not
> read from the server at runtime. "Setting it on the server" only works because
> you build the client on the server (the Caddy `Dockerfile` runs `ng build`). If
> you ever ship a pre-built bundle, an empty/placeholder credential will silently
> break relay for users behind symmetric NAT. Options, best first:
> 1. **Runtime ICE config (recommended long-term):** add a backend endpoint that
>    returns `iceServers` (ideally short-lived coturn REST credentials via
>    `use-auth-secret`), and have the client fetch it at startup. Removes the
>    build-time secret entirely.
> 2. **Build-arg injection:** pass `--build-arg TURN_CREDENTIAL=…` and substitute
>    it into `environment.ts` during the Docker build (snippet in the Dockerfile).
> 3. **Edit `environment.ts` in place** on the server before building (simplest,
>    but the secret lives in a tracked file — don't push it back to git).

## 4. First deploy

```bash
# 0. Prereqs on the VDS (Ubuntu 22.04/24.04):
sudo apt-get update && sudo apt-get install -y docker.io docker-compose-plugin coturn

# 1. Lay out the deploy root
sudo mkdir -p /opt/voiceroom && cd /opt/voiceroom
git clone <gvoice-client-url> client
git clone <gvoice-server-url> server
cp server/docs/deploy/{Dockerfile,Caddyfile,docker-compose.yml} .
cp server/docs/deploy/.env.example .env
mkdir -p certs && cp /path/to/certificate_fullchain.crt /path/to/certificate.key certs/

# 2. Fill in secrets
#    - edit .env               → GVOICE_ADMIN_PASSWORD=<long random>
#    - edit client/src/environments/environment.ts → TURN credential (see §3)
#    - edit Caddyfile          → your domain
$EDITOR .env client/src/environments/environment.ts Caddyfile

# 3. Configure & start TURN (see server/docs/deploy/coturn/turnserver.conf)
sudo cp server/docs/deploy/coturn/turnserver.conf /etc/turnserver.conf
sudo sed -i 's/#TURNSERVER_ENABLED=1/TURNSERVER_ENABLED=1/' /etc/default/coturn
sudo systemctl enable --now coturn

# 4. Firewall — allow SSH FIRST, then the rest
sudo ufw allow 22/tcp
sudo ufw allow 80,443/tcp
sudo ufw allow 443/udp                 # HTTP/3
sudo ufw allow 3478/tcp && sudo ufw allow 3478/udp   # TURN/STUN
sudo ufw allow 49152:65535/udp         # TURN relay range
sudo ufw enable

# 5. Build & run
docker compose up -d --build
docker compose ps
docker compose logs -f api
```

Open `https://<your-domain>` — the lobby should list the default rooms
(`General`, `Gaming`, `Music`).

## 5. Redeploy

```bash
cd /opt/voiceroom
git -C client pull && git -C server pull
docker compose up -d --build      # rebuilds client bundle + api image
```

Room/participant state is in memory and resets on `api` restart (expected). Chat
history survives because it is written to the `chat_history` volume.

## 6. Configuration reference (backend)

Set via environment variables (preferred) or `appsettings.json`. See
[`configuration.md`](./configuration.md) for the full list. The essentials:

| Key | Default | Deploy value |
|---|---|---|
| `AdminPassword` | `change-me-123` | **override** via `.env` |
| `ChatHistoryPath` | `chat-history` | `/data/chat-history` (mounted volume) |
| `Cors:AllowedOrigins:0` | `http://localhost:4200` | not needed in prod — client is same-origin behind Caddy; leave as-is or set your domain |
| `ASPNETCORE_URLS` | — | `http://0.0.0.0:5293` |

## 7. Verification checklist

- [ ] `https://domain` loads the lobby (valid TLS, no mixed-content warnings).
- [ ] Browser devtools → Network → `wss://domain/api/hub/signaling` connects (101).
- [ ] Two browsers in the same room hear each other (P2P path).
- [ ] A client behind mobile/CGNAT still connects → confirms TURN works
      (`chrome://webrtc-internals` shows a `relay` candidate pair).
- [ ] Admin can create a room (correct `GVOICE_ADMIN_PASSWORD`).
- [ ] `docker compose restart api` → chat history persists, rooms reset.

## 8. Known deployment gotchas (verified against current code)

1. **`/api` prefix stripping is mandatory.** The client uses `rootUrl: '/api'`,
   the backend maps the hub at `/hub/signaling`. The Caddyfile's
   `uri strip_prefix /api` bridges them. Without it → 404 on the hub.
2. **Admin password default.** If `AdminPassword` isn't overridden, it falls back
   to `change-me-123` (`appsettings.json`) / `default-secret` (hub) — anyone could
   create rooms. Always set it.
3. **Chat history is ephemeral without a volume.** Default `ChatHistoryPath` is a
   relative dir inside the container. The compose here mounts a `chat_history`
   volume; keep it or history vanishes on redeploy.
4. **SSR output is unused.** `ng build` emits `dist/gvoice-client/server` too, but
   only `browser/` is served (static via Caddy). Harmless; you may switch the
   client to a pure static build to speed up builds (see client `docs`).
5. **TURN credential is build-time** — see §3.
