# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Shared runtime state accessible by all screens.
extends Node

var bc: BrainCloudWrapper = null

var username: String = ""
var user_cx_id: String = ""

var lobby_id: String = ""
var lobby_members: Array[Dictionary] = []

var color_palette: Array[Color] = []
var lobby_types: Array[String] = []
var splotch_duration: int = -1

# Leaderboard ids — overridable via matching global properties (Design > Global Properties)
# so the boards can be created/renamed in the portal with no client rebuild; these are just
# the compiled-in defaults, matching cpp's globals.h. See LoginScreen._load_global_properties.
var points_leaderboard_id: String = "CursorParty_Points"
var points_leaderboard_id_quarterly: String = "CursorParty_Points_Quarterly"
var coverage_leaderboard_id: String = "CursorParty_HighestCoverage"
var coverage_leaderboard_id_quarterly: String = "CursorParty_HighestCoverage_Quarterly"

var selected_lobby_type: String = ""
var selected_protocol: String = "WS"
var use_ping_data: bool = false
var my_color_index: int = 0

# Local optimistic "am I ready" flag — the source of truth for our own readiness display
# (both Lobby ready-up and Match Summary's rematch queue use it). We don't trust
# lobby_members' entry for our own cxId here since it's briefly stale after any update_ready
# call, until the server echoes it back via MEMBER_UPDATE.
var user_is_ready: bool = false

var server_info: Dictionary = {}
var my_net_id: int = -1

var lobby_owner_cx_id: String = ""
var ping_data: Dictionary = {}       # region → ms, measured before lobby join
var game_start_time_ms: int = 0      # Unix epoch ms when match started (set by host, synced to non-hosts)

# This-lobby chat history (Lobby service send_signal — separate from the Chat-service global
# channel). Owned here (not by LobbyScreen) so it survives LobbyScreen being recreated between
# rounds. Array of {from, text, is_me}.
var lobby_chat_history: Array = []

# Global chat (Chat service "gl" channel) — channel resolution + history + the live RTT push
# subscription all live here (not on GlobalChatPanel) so they survive that panel being
# recreated on every screen transition. Array of {from_name, text}.
var global_chat_channel_id: String = ""
var global_chat_history: Array = []

# Match result (BCLOUD-14472/14489/14490) — the round's coverage/win-ranking snapshot plus
# leaderboard deltas. Lives here instead of on GameScreen so it survives the jump to
# MatchSummaryScreen. Entries are {cx_id, rank, coverage_pct, beaten, lb_delta}.
var round_number: int = 0
var match_result_round: int = -1
var match_result_entries: Array = []
var leaderboard_posted_round: int = -1

# Default 40-colour palette, matching the js/dotnet/C#-Godot CursorParty clients. Gets
# overwritten at runtime from the "Colors" global property — seeding it here just means we
# never end up with an empty palette (modulo-by-zero) or a short one (missing swatches) if
# that property is undefined.
const DEFAULT_COLOR_HEX := [
	"FF3333", "FF8800", "FFD700", "88FF00", "00EE44", "00DDDD", "00AAFF", "3355FF", "AA00FF", "FF00BB",
	"FF5566", "FFAA00", "AADD00", "00FF88", "00FFCC", "0088FF", "8833FF", "FF44AA", "77FF33", "FF6688",
	"FF9999", "FFCC88", "FFFF88", "AAFFAA", "88FFEE", "AABBFF", "DDBBFF", "FFBBDD", "CCFFDD", "FFEECC",
	"CC1133", "CC5500", "88AA00", "228855", "009999", "3366AA", "7744CC", "AA3366", "AA6633", "7788AA",
]

func _ready() -> void:
	color_palette = default_palette()

static func default_palette() -> Array[Color]:
	var out: Array[Color] = []
	for h in DEFAULT_COLOR_HEX:
		out.append(Color.html(h))
	return out

# Test-time URL overrides (Web export only) — lets a shared link pre-select the lobby type
# and/or relay protocol, e.g. ?lobbyType=CursorPartyGameLift&protocol=wss. Mirrors the React
# RelayTestApp's same convention. Uses JavaScriptBridge.eval rather than create_object()/.get()
# because JavaScriptObject's "get" would collide with Godot's own Object.get(property) method.
static func get_url_param(key: String) -> String:
	if not OS.has_feature("web"):
		return ""
	var result = JavaScriptBridge.eval(
		"new URLSearchParams(window.location.search).get('%s') || ''" % key, true
	)
	return str(result) if result != null else ""

static func get_url_lobby_type() -> String:
	return get_url_param("lobbyType")

static func get_url_protocol() -> String:
	return get_url_param("protocol")

# Parse the "Colors" global property into a palette. Tolerates both formats seen in
# the wild: a JSON array of hex strings (["#FF3333", ...]) and a bare comma-separated
# list (FF3333,FF8800,...), with or without a leading '#'. Returns [] if unusable so
# the caller can keep the default rather than blanking the palette.
static func parse_palette(raw: String) -> Array[Color]:
	var out: Array[Color] = []
	raw = raw.strip_edges()
	if raw.is_empty():
		return out
	var tokens: Array = []
	if raw.begins_with("["):
		var parsed = JSON.parse_string(raw)
		if parsed is Array:
			tokens = parsed
	else:
		tokens = Array(raw.split(",", false))
	for t in tokens:
		var h := String(t).strip_edges().lstrip("#")
		if Color.html_is_valid(h):
			out.append(Color.html(h))
	return out
