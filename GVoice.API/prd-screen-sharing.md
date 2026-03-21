# VoiceRoom: Screen Sharing PRD

## 1. Executive Summary
- **Product Name:** VoiceRoom
- **Goal:** Enable users to share their screen (and system audio) with other participants in the room via a peer-to-peer WebRTC video stream.
- **Target Audience:** Small groups collaborating or presenting content in real-time.

## 2. Problem Statement
- VoiceRoom is currently audio-only. Users need to share visual content (documents, code, browser windows) without leaving the application or using a secondary tool.
- P2P screen sharing is critical for low-latency collaboration in trusted groups.

## 3. Functional Requirements
- **F1: Local Screen Capture:**
  - Integrated `getDisplayMedia` API call to prompt user for screen/window selection.
  - Default profile: **Balanced (720p / 30fps)** for optimal movement and clarity.
  - Optional "Share System Audio" support (if supported by browser/OS).
  - Silent close if browser permission dialog is cancelled.
- **F2: Signaling & State:**
  - SignalR event to notify all peers when a user starts or stops sharing.
  - `Participant` state updated with `IsSharingScreen: boolean`.
- **F3: Peer-to-Peer Transmission:**
  - Add video/audio tracks to all existing `RTCPeerConnection` instances.
  - Trigger renegotiation (New Offer/Answer cycle) with all peers.
  - Ensure cleanup (removing tracks) when sharing stops.
- **F4: Sharing User UI:**
  - "Share Screen" button in the Top Bar.
  - Active state indicator in the UI.
  - Handle "Stop Sharing" from browser native controls.
  - **Single Active Sharer Enforcement:** If another user is already sharing, the system blocks the new share and displays an error message ("Someone else is already sharing their screen").
- **F5: Viewer UI:**
  - "Stream" badge appears on the participant card of the sharer.
  - Clicking the participant card or badge opens a large-scale overlay/stage viewer.
  - Support for "Full Screen" mode in the viewer.

## 4. UI/UX Requirements (Monochrome Focus)
- **Stream Badge:** Minimalist indicator (e.g., `[STREAM]`) with the primary theme color.
- **Overlay Viewer:**
  - Centered modal/overlay with semi-transparent background.
  - Close button and volume control (if audio shared).
  - Maintains aspect ratio of the shared content.
- **Interactive States:** Clickable participant card when sharing is active.

## 5. Technical Constraints
- **Concurrency:** Limited to **Single Active Sharer** (prevents bandwidth congestion in P2P mesh). If another user starts sharing, the previous share is stopped (or the start is blocked).
- **Mobile Support:** **View Only**. Mobile browsers will be able to view shared streams but cannot broadcast their own.
- **Performance:** P2P bandwidth will increase; use reasonable frame rate (15-30fps) and resolution (720p/1080p).

## 6. Out of Scope
- Multiple concurrent screen shares.
- Mobile broadcasting (Sharing from iOS/Android).
- Drawing/Annotation on shared screen.
- Remote control.

## 7. Success Metrics
- Screen share starts and appears to all participants within < 2 seconds of selection.
- Video latency matches audio latency (P2P synchronization).
- Graceful cleanup of resources when sharing stops (no "frozen" video frames).

## 8. Development Task List

### Phase 1: Foundation (Backend & Shared)
- **T-301: [Backend] Model & Events Update**
  - **Description:** Ensure `Participant.cs` has `IsSharingScreen` and `SignalREvents.cs` has `SharingScreen` constant.
  - **Validation:** Check `Participant.cs` and `SignalREvents.cs` for properties.
- **T-302: [Backend] SignalingHub State Handling**
  - **Description:** Update `SignalingHub.UpdateState` to accept `sharingScreen` type and broadcast `PeerStateUpdated`.
  - **Validation:** Send SignalR message `UpdateState('sharingScreen', true)` and verify broadcast.

### Phase 2: Core Logic (Frontend Service Layer)
- **T-303: [Frontend] Service State Update**
  - **Description:** Update Angular `Participant` interface and `SignalRService` to handle `SharingScreen` state.
  - **Validation:** Verify `SignalRService` emits `peerStateUpdated$` for screen sharing.
- **T-304: [Frontend] Media Capture Implementation**
  - **Description:** Implement `startScreenShare()` in `WebRtcService` using `navigator.mediaDevices.getDisplayMedia`.
  - **Validation:** Clicking a (temporary) button triggers browser screen share prompt.
- **T-305: [Frontend] P2P Track Management**
  - **Description:** Update `WebRtcService` to add video/audio tracks from screen share to all active `RTCPeerConnection` instances.
  - **Validation:** Use `pc.addTrack()` and verify tracks are added to peer connections in browser console.
- **T-306: [Frontend] Renegotiation Logic**
  - **Description:** Trigger `onnegotiationneeded` or manually create new Offer/Answer when screen tracks are added.
  - **Validation:** Verify `onnegotiationneeded` is fired and signaling messages are sent to peers.

### Phase 3: UI/UX (Frontend Components)
- **T-307: [Frontend] Share Screen Control**
  - **Description:** Add "Share Screen" icon button to the Top Bar. Implement logic to block sharing if `ParticipantService` shows someone else is already sharing.
  - **Validation:** Button is visible; clicking starts share; blocked if active sharer exists.
- **T-308: [Frontend] Participant Stream Badge**
  - **Description:** Add `[STREAM]` badge to `ParticipantCardComponent` when `isSharingScreen` is true.
  - **Validation:** Badge appears/disappears correctly based on participant state.
- **T-309: [Frontend] Overlay Viewer Component**
  - **Description:** Create `ScreenShareOverlayComponent` with a `<video>` element. Show overlay when a sharing participant is clicked.
  - **Validation:** Video stream renders in overlay; can be closed; supports full screen.

### Phase 4: Validation & Polish
- **T-310: [Validation] End-to-End P2P Test**
  - **Description:** Open two browser tabs, join a room, share screen in one, view in the other.
  - **Validation:** Stream is visible with low latency and synchronized audio.
- **T-311: [UI] Monochrome Styling & Feedback**
  - **Description:** Ensure stream badge and overlay align with the project's monochrome theme and tight grid.
  - **Validation:** Visual consistency check across themes.
