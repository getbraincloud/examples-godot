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

### 1. Install the brainCloud GDScript SDK

Download the **brainCloud GDScript SDK** from the [Godot Asset Library](https://godotengine.org/asset-library/asset/5196).

To install it into this project:

1. Open **AssetLib** inside the Godot editor (top-centre tab).
2. Search for **brainCloud** and click **Download** on the result.
3. When the import dialog appears, make sure the destination is `addons/braincloud/`, then click **Install**.

Alternatively, download the zip from the asset library page, extract it, and copy the `addons/braincloud/` folder directly into `RelayTestApp-GDScript/`.

> **Note:** The addon must be a real copy inside the project folder — Godot's class name scanner does not follow symlinks outside the project root.

After installing, enable the plugin: **Project → Project Settings → Plugins → brainCloud → Enable**.

### 2. Enter your app credentials

The brainCloud plugin includes an editor configuration window. With the project open in Godot, go to:

**Project → Tools → brainCloud Config...**

Fill in your **App ID**, **App Secret**, and **Server URL** from the [brainCloud Portal](https://portal.braincloudservers.com/), then click **Save**. Credentials are stored in `project.godot` under `braincloud/config/*` and can also be edited directly via **Project → Project Settings → braincloud**.

> **Security note:** `project.godot` is committed to version control by default. Add it to `.gitignore`, or use `Scripts/Ids.gd` as a git-ignored override file — the app reads project settings first and falls back to `Ids.gd` if a setting is empty.

The plugin window also includes quick links to the Portal, API Reference, documentation, and the GDScript SDK on GitHub.

### 3. Configure your brainCloud app

In the brainCloud Portal, ensure your app has:

- **RTT enabled** — required for lobby and relay functionality
- **At least one Lobby Type** configured (e.g. `CursorPartyv2`) with a relay server region
- **Global App Properties** set:
  - `Colours` — comma-separated hex colour values (e.g. `ff0000,00ff00,0000ff,...`)
  - `AllLobbyTypes` — JSON object mapping lobby type keys to `{"lobby": "TypeName"}` entries
  - `SplotchDuration` — integer seconds a paint splotch persists (`-1` for forever)

### 4. Open in Godot

Open the project folder (`RelayTestApp-GDScript/`) in Godot 4.5.

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
    └── braincloud/               ← brainCloud GDScript SDK (install from Asset Library)
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

### The addon must be inside the project

The brainCloud GDScript addon lives at `addons/braincloud/` inside the project. Godot's class name scanner does not follow symlinks outside the project root, so a symlink to a shared location will break class resolution. Always install it as a real copy (via the Asset Library or by copying the folder manually).

---

## Companion Projects

| Project | Language | Location |
|---|---|---|
| Relay Test App (C#) | C# / Godot .NET | `../RelayTestApp/` |
| Relay Test App (C++) | C++ / ImGui | `../../cpp-examples/relaytestapp/` |
| brainCloud GDScript SDK | GDScript | `../../braincloud-gdscript/` |

---

For brainCloud documentation and API reference, visit [getbraincloud.com/apidocs](https://getbraincloud.com/apidocs/).
