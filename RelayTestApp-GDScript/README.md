# brainCloud Relay Test App — GDScript

A multiplayer demo built with **Godot 4.5 (GDScript)** that demonstrates brainCloud's Matchmaking and Relay services. Players can see each other's mouse cursors move in real time, click to fire shockwaves that leave persistent paint splotches on a shared canvas, and play in timed 90-second rounds.

This project is the GDScript equivalent of the [C# Relay Test App](../RelayTestApp/) and the [C++ Relay Test App](../../cpp-examples/relaytestapp/), and is designed to be compared against those implementations.

---

## Requirements

- **Godot 4.5** (standard build — no .NET required)
- A **brainCloud account** with an app configured for relay matchmaking
- At least **two players** to test multiplayer features (can run two instances locally)

---

## Setup

### 1. Enter your App credentials

Open `Scripts/Ids.gd` and replace the placeholder values with your own app's credentials from the [brainCloud Portal](https://portal.braincloudservers.com/):

```gdscript
const SERVER_URL  := "https://api.braincloudservers.com/dispatcherv2"
const APP_SECRET  := "your-app-secret-here"
const APP_ID      := "your-app-id-here"
const APP_VERSION := "1.0"
```

> **Note:** `Ids.gd` is an autoload singleton. Do not commit real credentials to public repositories.

### 2. Configure your brainCloud app

In the brainCloud Portal, ensure your app has:

- **RTT enabled** — required for lobby and relay functionality
- **At least one Lobby Type** configured (e.g. `CursorPartyv2`) with a relay server region
- **Global App Properties** set:
  - `Colours` — comma-separated hex colour values (e.g. `ff0000,00ff00,0000ff,...`)
  - `AllLobbyTypes` — JSON object mapping lobby type keys to `{"lobby": "TypeName"}` entries
  - `SplotchDuration` — integer seconds a paint splotch persists (`-1` for forever)

### 3. Open in Godot

Open the project folder (`RelayTestApp-GDScript/`) in Godot 4.5. The brainCloud addon is included under `addons/braincloud/` — no additional installation is required.

---

## Running the App

Press **Run** (F5) in Godot. The app launches at 1280×720.

### Flow

1. **Login screen** — enter a username and password. New accounts are created automatically (Universal authentication). Check **Remember Me** to save credentials between runs.
2. **Main Menu** — choose a Lobby Type and transport protocol, then click **Find Game**.
   - **Protocol options:** `WS` (WebSocket), `WSS` (WebSocket Secure), `UDP`, `TCP`
   - **With Ping Data:** measures latency to each relay region before joining, allowing the server to pick the best region
3. **Lobby** — wait for other players to join. Pick a colour from the palette. The host clicks **Start** when ready.
4. **Game screen** — 90-second round. Move your mouse to broadcast your cursor position. Left-click to fire a shockwave and leave a paint splotch. The host can **Clear Splotches** or **End Match** at any time.
5. After the match, everyone returns to the Lobby for the next round without re-matchmaking.

---

## Project Structure

```
RelayTestApp-GDScript/
├── Scripts/
│   ├── Ids.gd                    ← App credentials (edit this)
│   ├── AppState.gd               ← Shared runtime state (autoload)
│   ├── BrainCloudBootstrap.gd    ← SDK initialisation (autoload)
│   ├── Main.gd                   ← Screen orchestrator + relay/RTT lifecycle
│   └── Screens/
│       ├── LoginScreen.gd        ← Universal auth + global properties load
│       ├── MainMenuScreen.gd     ← Lobby type / protocol / ping selection
│       ├── LobbyScreen.gd        ← Lobby wait room + colour picker
│       ├── LobbyMember.gd        ← Per-player row widget in lobby
│       └── GameScreen.gd         ← Game loop, relay send/receive, UI
│   └── Components/
│       ├── PlayerCursor.gd       ← Remote player cursor node
│       ├── Shockwave.gd          ← Expanding ring animation
│       └── Splotch.gd            ← Persistent paint mark with spring animation
├── Scenes/                       ← .tscn files mirroring Scripts/
├── Assets/
│   └── PaintSplatter1.png        ← Splotch texture (white-on-transparent)
└── addons/
    └── braincloud/               ← brainCloud GDScript SDK (copied, not symlinked)
```

---

## Key Implementation Notes

### Relay message protocol

All game messages are JSON sent over the brainCloud Relay connection:

| `op` | Direction | Description |
|---|---|---|
| `game_start` | host → all | Broadcasts the authoritative match start timestamp |
| `move` | any → all | Normalised cursor position `{x, y}` in [0, 1] |
| `shockwave` | any → masked | Click position `{x, y, angle}` (angle kept in sync for identical splotch rendering) |
| `splotch_sync` | host → joiner | Sends existing splotches to a player who joins mid-match |
| `clear_splotches` | host → all | Clears the canvas |
| `relay_ping` | any → all | Broadcasts each player's live relay RTT in ms |
| `end_match` | host → all | Uses the SDK `end_match()` opcode (not a plain relay message) |

### End Match

The host calls `relay_service.end_match(...)` which sends the binary `CL2RS_ENDMATCH` (opcode 6) packet to the relay server. The relay server then broadcasts an `END_MATCH` **system message** to all players, triggering a clean transition for everyone. This matches the C++ `endMatch()` SDK call exactly.

Do **not** use a plain `send()` call for ending a match — the relay server would not know to close the room, leaving non-host players stranded.

### Splotch angle synchronisation

When any player fires a shockwave, the rotation angle of the resulting splotch is chosen locally and included in the relay message. All clients render the splotch at the same angle rather than picking random values independently. This ensures visual consistency across clients.

### Join-in-progress (JIP)

When a player joins a game already in progress, the host sends existing splotches in size-bounded chunks via `splotch_sync`. The first chunk carries `"first": true` so the receiver clears its canvas before appending.

### `@onready` and `add_child` ordering

When instantiating component scenes (`Shockwave`, `Splotch`, `PlayerCursor`), always call `add_child()` **before** `setup()`. The `@onready` variable bindings are resolved when the node enters the scene tree, so calling `setup()` first results in nil dereferences.

### The addon must be a real copy

The brainCloud GDScript addon lives at `addons/braincloud/` inside the project. Godot's class name scanner does not follow symlinks outside the project root, so a symlink to a shared location will break class resolution. Keep it as a hard copy.

---

## Companion Projects

| Project | Language | Location |
|---|---|---|
| Relay Test App (C#) | C# / Godot .NET | `../RelayTestApp/` |
| Relay Test App (C++) | C++ / ImGui | `../../cpp-examples/relaytestapp/` |
| brainCloud GDScript SDK | GDScript | `../../braincloud-gdscript/` |

---

For brainCloud documentation and API reference, visit [getbraincloud.com/apidocs](https://getbraincloud.com/apidocs/).
