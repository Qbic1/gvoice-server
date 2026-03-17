# VoiceRoom v1.5: Multi-Room & Admin Management (PRD)

## 1. Overview
VoiceRoom is evolving from a single-fixed-room tool into a multi-room platform. This update introduces a "Lobby" for room discovery, admin-only room creation, and per-room password protection.

## 2. Feature Requirements

### 2.1 Global Admin Authentication
- **Admin Password:** Stored in `appsettings.json` on the server.
- **Functionality:** Unlocks the "Create Room" button in the Lobby.
- **Persistence:** Admin status is stored in `localStorage` once verified.

### 2.2 Room Lobby
- **Visibility:** The root URL (`/`) displays a list of all active rooms.
- **Discovery:** Users can see Room Name and current participant count.
- **Access:** Clicking a room navigates the user to `/room/:roomId`.

### 2.3 Per-Room Security & Limits
- **Passwords:** Every room created by an admin MUST have a password. 
- **Validation:** Users must enter the correct Room Password on the "Join" screen to enter the session.
- **Capacity:** Hard cap of **10 concurrent users** per room.

### 2.4 Dynamic Room Creation (Admin Only)
- **Input:** Admins provide Room Name and Room Password.
- **ID Generation:** Room IDs are automatically generated based on the name (Slugified).
- **Persistence:** Rooms are held in-memory and persist until server restart.

## 3. Technical Implementation

### 3.1 Backend (ASP.NET Core / SignalR)
- **State:** `SignalingHub` tracks a `ConcurrentDictionary<string, Room>` where `Room` contains `Name`, `HashedPassword`, and `Participants`.
- **Endpoints:**
    - `POST /api/admin/verify`: Validates the Global Admin Password.
    - `GET /api/rooms`: Returns the list of active rooms (publicly visible).
- **Hub Methods:** 
    - `Join(roomId, password, displayName, isListenOnly)`: Validates both room password and capacity.

### 3.2 Frontend (Angular)
- **LobbyComponent:** Displays the room list and "Create Room" dialog (Admin only).
- **JoinRoomComponent:** Updated to include a "Room Password" field.
- **AdminService:** Handles global admin state and password verification.
- **Routing:** 
    - `/` -> Lobby
    - `/room/:roomId` -> Join/Voice Room

---
**Status:** Approved for Implementation (March 2026)
