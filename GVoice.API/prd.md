**VoiceRoom**

Product Requirements Document

  ------------------- ---------------------------------------------------
  **Version**         1.2 --- Revised

  **Date**            March 2026

  **Status**          Decisions Locked

  **Platform**        Web Browser (Tauri desktop deferred to v2)

  **Hosting**         Self-hosted (user's Linux server via Docker
                      Compose)
  ------------------- ---------------------------------------------------

1\. Overview

VoiceRoom is a lightweight, self-hosted voice communication tool for
small groups of trusted users (up to 6 concurrent). It runs in a web
browser. Users join a single, fixed private room via a secret URL and
can communicate via real-time WebRTC voice, push-to-talk, and a
persistent in-session text chat sidebar. No account creation or
authentication is required. The desktop app (Tauri) has been deferred to
v2.

2\. Goals & Non-Goals

2.1 Goals

-   Enable real-time, low-latency voice communication for up to 6
    concurrent users via WebRTC P2P mesh.

-   Provide push-to-talk (PTT) mode as an opt-in alternative to
    open-mic.

-   Include a text chat panel for sharing links and notes during a
    session (in-memory, session-scoped).

-   Show live indicators: who is speaking, who is muted, who is
    connected, who is listen-only.

-   Run in modern web browsers (Chrome, Firefox, Edge, Safari).

-   Be fully self-hosted on a single Linux server with a one-command
    Docker Compose deploy.

-   Recover gracefully from server restarts with a visible disconnect
    banner and manual rejoin button.

-   Support listen-only mode for users who deny microphone permission
    (can hear and chat, cannot transmit).

2.2 Non-Goals

-   No user authentication or account system in v1.

-   No video support in v1.

-   No message history persistence beyond the current session.

-   No mobile app (iOS / Android) in v1.

-   No desktop app (Tauri/Electron) in v1 --- deferred to v2.

-   No dynamic room creation --- one fixed private room only.

-   No SFU / mediasoup support in v1 --- hard cap of 6 users via P2P
    mesh.

-   No TURN server provisioning in v1 --- users assumed to be on simple
    NATs or VPN.

-   No audio output device selection --- system default output only.

-   No display name changes after joining --- name is locked for the
    session.

3\. Users & Context

The primary audience is a small, closed group of friends or
collaborators who trust each other and share the app URL out-of-band.
Access is controlled entirely by keeping the URL secret --- no auth
layer is needed.

  ------------------- ---------------------------------------------------
  **Group size**      2 -- 6 concurrent users (hard cap in v1)

  **Access model**    Fixed private room --- secret/unguessable URL only

  **Trust model**     High trust; no auth required

  **Deployment**      Self-hosted on user-owned Linux VPS or home server
                      via Docker Compose
  ------------------- ---------------------------------------------------

4\. Feature Requirements

  --------------- ---------------------------------- -------------- -------------
  **Feature**     **Description**                    **Priority**   **Status**

  Real-time voice WebRTC P2P mesh audio between all  P0             In scope
                  participants. Open-mic is the                     
                  default mode.                                     

  Push-to-talk    Togglable PTT mode: mic transmits  P0             In scope
  (PTT)           only while Spacebar (or rebound                   
                  key) is held. Suppressed when chat                
                  input is focused.                                 

  Mute / Unmute   Each user can mute their own mic.  P0             In scope
                  Their card immediately reflects                   
                  mute state to all others.                         

  Speaking        Animated visual pulse around a     P0             In scope
  indicators      participant's card when their                     
                  audio stream is active. Suppressed                
                  if user is muted (mute icon takes                 
                  visual priority).                                 

  Participant     Panel showing all connected users  P0             In scope
  list            (max 6) with display name, mute                   
                  status, speaking indicator,                       
                  listen-only badge, and deafen                     
                  indicator.                                        

  Listen-only     If mic permission is denied, user  P0             In scope
  mode            joins as listen-only: can hear and                
                  use text chat but cannot transmit                 
                  audio. Others see a listen-only                   
                  badge and greyed-out mic toggle.                  

  Text chat       Chat panel alongside participant   P1             In scope
                  list. Messages broadcast via                      
                  SignalR, in-memory only. Messages                 
                  sent during a disconnect are not                  
                  replayed on rejoin.                               

  Disconnect      On SignalR connection loss:        P1             In scope
  banner          full-width overlay banner with a                  
                  manual Rejoin button. No                          
                  auto-reconnect loop. All in-memory                
                  state cleared.                                    

  Display name    Prompted on first join. Persisted  P1             In scope
                  in localStorage, pre-filled on                    
                  return visits. Auto-assigned                      
                  random adjective-noun if left                     
                  blank. Locked for the session.                    

  Join / leave    Subtle audio chime when a          P2             In scope
  sounds          participant joins or leaves.                      

  Deafen          User can mute all incoming audio.  P2             In scope
                  Others see a deafened indicator on                
                  that user's card.                                 

  Configurable    Settings modal (triggered from top P2            In scope
  PTT key         bar) allows rebinding the PTT key                 
                  from Spacebar to any key.                         

  Theme System    8 built-in themes (Purple, Ocean,  P2             In scope
                  Rose, Amber + Dark variants)                      
                  using semantic CSS tokens. Persistent             
                  to localStorage via ThemeService.                 

  Screen Sharing  Users can share their screen (video)  P2             In scope
                  with all other participants in the                
                  room. Large overlay for viewing.                                           
  --------------- ---------------------------------- -------------- -------------

5\. Technical Architecture

5.1 Stack Overview

  ------------------ ----------------------------------------------------
  **Layer**          **Technology**

  Frontend           Angular 21 (standalone components) + TypeScript +
                     Vite build. CSS Variables / Semantic Tokens for 
                     styling (Themeable).

  Voice transport    WebRTC P2P mesh --- sufficient for up to 6 users. No
                     SFU in v1.

  Signaling server   ASP.NET Core (minimal APIs) + SignalR for
                     WebSocket-based offer/answer relay and ICE candidate
                     exchange.

  Text chat          SignalR room broadcast --- in-memory only, no
                     database required.

  Reverse proxy      Caddy --- automatic HTTPS via Let's Encrypt, zero
                     manual certificate management.

  Deployment         Docker Compose --- single command to bring up Caddy,
                     ASP.NET Core, and Angular static files.

  Process manager    Docker Compose manages process lifecycle; no
                     PM2/systemd needed inside containers.

  STUN               Public Google STUN servers (stun.l.google.com). No
                     self-hosted STUN or TURN in v1.
  ------------------ ----------------------------------------------------

5.2 Angular Service Architecture

Responsibilities are split into one service per concern, kept as
injectable singletons:

  -------------------- ----------------------------------------------------
  **Service**          **Responsibilities**

  SignalRService       Owns the HubConnection lifecycle. Emits observables
                       for connection state changes, incoming signaling
                       messages (offer, answer, ICE candidate), participant
                       join/leave events, and chat messages.

  WebRtcService        Manages RTCPeerConnection instances for each remote
                       peer. Handles offer/answer negotiation, ICE
                       candidate exchange (via SignalRService), and
                       MediaStreamTrack.enabled toggling for mute/PTT.
                       Tears down a peer's connection on hub notification.

  ParticipantService   Maintains the authoritative list of connected
                       participants and their state (mute, speaking,
                       deafen, listen-only). Exposes a Signal or
                       BehaviorSubject consumed by the participant list
                       component.

  ChatService          Holds in-memory chat message history for the current
                       session. Receives messages from SignalRService and
                       exposes them to the chat panel component. Clears
                       state on disconnect.

  ThemeService         Manages reactive theme state using Angular Signals.
                       Persists preference to localStorage and applies
                       data-theme attributes to the document root.
                       Eagerly initialized via APP_INITIALIZER.
  -------------------- ----------------------------------------------------

5.3 Key Technical Decisions

-   WebRTC mesh (full P2P) for groups up to 6 users. Hard cap enforced
    at the SignalR hub --- a user is counted as 'in the room' after they
    submit their display name.

-   Peer disconnect handling: SignalR hub broadcasts a PeerLeft
    notification to all remaining clients; each client tears down that
    peer's RTCPeerConnection and removes their participant card.

-   No database --- all state (participants, messages) is held in memory
    on the SignalR hub and reset on server restart.

-   HTTPS + WSS mandatory: browsers block microphone access on
    non-secure origins. Caddy handles automatic TLS termination and
    renewal via Let's Encrypt --- no manual certificate management
    required.

-   Push-to-talk is client-side only: keydown/keyup events toggle
    MediaStreamTrack.enabled. Zero server involvement. PTT listener
    suppressed when the chat input field is focused.

-   Listen-only mode: if getUserMedia() fails or permission is denied,
    the user joins without a local audio track. The hub is notified of
    listen-only status; all clients reflect this on the participant
    card.

-   Mute indicator priority: if a user is muted, the mute icon is shown
    and the speaking ring is suppressed, even if audio activity is
    detected on their stream.

-   Display name persisted in localStorage, pre-filled on return visits.
    Empty submission auto-assigns a random adjective-noun name. Name is
    locked for the session --- no mid-session rename.

-   Disconnect handling: Angular listens for SignalR HubConnection state
    changes. On disconnect, a full-screen overlay banner appears with a
    manual Rejoin button. No auto-reconnect. All in-memory state
    cleared.

-   Chat replay: messages sent during a user's disconnect are not
    replayed on rejoin. The chat panel simply starts fresh from the
    point of reconnection.

-   STUN: public Google STUN servers used for NAT traversal. TURN out of
    scope for v1 --- target users assumed to be on home/office NATs or
    VPN/Tailscale.

5.4 TLS Recommendation

Use Caddy as the reverse proxy instead of nginx. Caddy handles Let's
Encrypt certificate issuance and automatic renewal with zero
configuration beyond specifying the domain name in the Caddyfile. This
eliminates the need to manage certbot cron jobs, certificate paths, or
nginx reload hooks --- a significant reduction in ops overhead for a
self-hosted deployment.

6\. Docker Compose Architecture

Three containers, brought up with a single docker compose up -d:

-   caddy --- Caddy reverse proxy. Terminates TLS, proxies /api and /hub
    to the ASP.NET Core container, and serves Angular static files from
    a shared volume.

-   api --- ASP.NET Core minimal API + SignalR hub. Exposes the
    signaling hub and enforces the 6-user room cap.

-   frontend --- Build-only container that compiles the Angular/Vite app
    into a shared volume consumed by Caddy.

7\. UX & Interface

7.1 Design Direction

Modern and utilitarian. Functionality over decoration. Semantic token-based
theme system with 8 light/dark presets (Purple, Ocean, Rose, Amber, etc.).
Consistent use of CSS variables for all colors, ensuring accessibility and
visual coherence across all themes.

7.2 Layout

-   Lobby: Brand lockup with room cards (avatars, capacity, join buttons).
    Redesigned for mobile as a bottom-sheet-style modal.

-   Top bar: room name, own mic toggle, PTT toggle, deafen button,
    settings button, disconnect/leave button.

-   Left / main panel (Desktop): participant list with name card, mute icon,
    speaking ring, deafen indicator, listen-only badge per user.

-   Right panel (Desktop): text chat with scrollable message history and input
    field.

-   Mobile Layout: Dedicated bottom navigation bar (Room / Chat / Settings).
    Layouts use dvh units for height stability. Settings appear inline
    without keyboard-specific controls.

7.3 Joining Flow

-   User opens the app URL in a browser.

-   A prompt asks for a display name. Pre-filled from localStorage on
    return visits. Blank submission auto-assigns a random name.

-   Browser requests microphone permission.

    -   Granted: user joins with full voice + chat capability.

    -   Denied: user joins as listen-only. A persistent notice informs
        them they cannot transmit audio.

-   User is counted as 'in the room' (against the 6-user cap) upon name
    submission.

-   User appears in all other participants' lists immediately.

-   If the room is at capacity (6 users), the user sees a 'Room full'
    message and cannot join.

7.4 Listen-Only Mode

-   Triggered when getUserMedia() is denied or fails.

-   Listen-only users can hear all participants and send/receive text
    chat.

-   Their participant card shows: a listen-only badge, a
    greyed-out/disabled mic toggle, and a tooltip on the mic toggle
    explaining why it is unavailable.

-   They cannot use PTT mode --- PTT toggle is hidden or disabled.

7.5 Push-to-Talk Behaviour

-   PTT toggle button in the top bar switches the user into PTT mode;
    open-mic is re-enabled when toggled off.

-   While in PTT mode, holding the PTT key activates the mic. Releasing
    it mutes immediately.

-   PTT keydown/keyup listeners suppressed when focus is inside the chat
    text input.

-   A clear visual indicator on the user's own card shows when PTT is
    actively transmitting.

-   PTT is unavailable in listen-only mode.

7.6 Settings Modal

-   Triggered by the settings button in the top bar.

-   Tabbed Layout:
    -   Theme: 4-column compact grid for selecting light/dark themes.
    -   Audio: Live input level canvas meter, noise gate sensitivity,
        audio enhancement toggles.
    -   Controls: PTT key rebinding (keyboard-style badge).

-   Mobile-specific: 'hideControls' input suppresses the Controls tab
    when used inline on mobile.

-   Modal overlays the full UI; dismissible via 'Done' button or Escape
    key. Includes a danger-soft 'Reset' button.

7.7 Disconnect Handling

-   When SignalR connection is lost, a full-width overlay banner
    appears: 'Server disconnected --- session ended.'

-   A prominent Rejoin button reloads the page and re-initiates the join
    flow.

-   All in-memory state (participant list, chat history) is cleared on
    disconnect.

-   No auto-reconnect loop --- rejoin is always a deliberate user
    action.

8\. Milestones

  ----------- --------------- -------------------------------------------------
  **Phase**   **Name**        **Deliverables**

  M1          Core Voice      ASP.NET Core + SignalR signaling server. Angular
                              frontend scaffold (standalone components,
                              Tailwind, service-per-concern architecture).
                              WebRTC P2P voice, participant list, open-mic &
                              mute/unmute, speaking indicators. Display name
                              with localStorage + auto-assign. Listen-only mode
                              for denied mic permission. 6-user room cap
                              enforced on name submission.

  M2          PTT + Chat +    Push-to-talk mode (Spacebar default,
              Resilience      chat-input-aware suppression). Text chat panel
                              via SignalR broadcast. Join/leave audio chimes.
                              Disconnect banner with Rejoin button. Peer
                              disconnect: hub notification + client-side
                              RTCPeerConnection teardown.

  M3          Polish + Deploy Deafen feature. Configurable PTT key (settings
                              modal). Docker Compose setup (Caddy + ASP.NET
                              Core + Angular static build). Deployment guide.
                              Responsive layout.

  M4          Desktop App     Tauri wrapper for macOS and Windows. Installer
              (v2)            builds. Verify microphone permission flow in
                              Tauri WebView. (Deferred from v1.)
  ----------- --------------- -------------------------------------------------

9\. Constraints & Assumptions

-   Single fixed room only --- no dynamic room creation in v1.

-   Hard cap of 6 concurrent users, enforced at the SignalR hub on
    display name submission.

-   The server must have a public IP and a valid domain name for Caddy's
    automatic TLS to function.

-   A TURN server is not provisioned in v1 --- users assumed to be on
    home/office NATs or VPN/Tailscale.

-   No abuse protection (banning, rate limiting) --- app is for trusted
    users only.

-   Access security relies entirely on the secrecy of the URL.

-   WebRTC P2P exposes participant IP addresses to each other ---
    acceptable given the high-trust user model.

-   Audio output device selection is out of scope --- system default
    audio output is used.

-   Chat messages are not replayed on reconnect --- users accept they
    will miss messages during a disconnect.

-   Claude Code is the primary development environment for building this
    project.

10\. Resolved Decisions

All open questions from v1.0 and v1.1, plus new decisions from the v1.2
interview:

  --------------------- -------------------------------------------------
  **Display name        Pre-filled from localStorage; auto-assigned
  persistence**         random name if blank. Locked for the session.

  **TURN server**       Out of scope for v1. Revisit if users report P2P
                        connection failures.

  **Desktop app**       Deferred to v2. Browser-only for v1.

  **PTT key config**    In-app settings modal (triggered from top bar).
                        Config file approach dropped.

  **Docker Compose**    Required at launch (M3). One-command deploy is a
                        hard requirement.

  **Mesh vs SFU**       P2P mesh only in v1. Hard cap at 6 users. SFU
                        deferred to v2 if needed.

  **Auth / access       Secret/unguessable URL only. No additional auth
  control**             layer.

  **Server restart UX** Disconnect banner with manual Rejoin button. No
                        auto-reconnect.

  **Visual design**     Minimal / utilitarian. Monochrome with functional
                        accent colours.

  **Frontend            Angular 21 standalone components + TypeScript +
  framework**           Tailwind CSS.

  **Signaling backend** ASP.NET Core minimal APIs + SignalR.

  **Reverse proxy /     Caddy (replaces nginx). Automatic Let's Encrypt
  TLS**                 --- no manual cert management.

  **Angular             One service per concern: SignalRService,
  architecture**        WebRtcService, ParticipantService, ChatService.

  **Frontend static     nginx (now Caddy) serves compiled Angular dist.
  serving**             ASP.NET Core handles API/SignalR only.

  **STUN servers**      Public Google STUN. No self-hosted STUN needed.

  **IP exposure**       Acknowledged and accepted --- all users are
                        trusted.

  **Peer disconnect     Hub broadcasts PeerLeft; each client tears down
  handling**            that peer's RTCPeerConnection.

  **Speaking + muted    Mute icon takes priority. Speaking ring
  conflict**            suppressed when user is muted.

  **Mic permission      User joins as listen-only (hear + chat, no
  denied**              transmit). Card shows listen-only badge, greyed
                        mic toggle, and tooltip.

  **Room cap timing**   User counted as 'in room' after submitting
                        display name.

  **Chat on reconnect** No message replay. Users accept missing messages
                        during disconnect.

  **Audio output        Out of scope. System default audio output used.
  selection**           

  **Display name        Name locked once joined. Change requires rejoin.
  changes**             

  **Tauri dev           Moot --- Tauri deferred to v2.
  workflow**            
  --------------------- -------------------------------------------------

11\. Success Metrics

-   End-to-end voice latency \< 150ms on LAN, \< 300ms over the
    internet.

-   Supports up to 6 concurrent users without audio degradation.

-   Zero dropped connections during a 2-hour session on a stable
    network.

-   Time-to-join (URL open to speaking) \< 10 seconds for a returning
    user.

-   Text messages delivered to all participants within 200ms.

-   Docker Compose deployment completes with a single command on a fresh
    Linux server.

-   Disconnect banner appears within 3 seconds of SignalR connection
    loss.

-   Listen-only users can join and participate in text chat within the
    same 10-second time-to-join target.