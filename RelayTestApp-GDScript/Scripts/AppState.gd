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

var selected_lobby_type: String = ""
var selected_protocol: String = "WS"
var use_ping_data: bool = false
var my_color_index: int = 0

var server_info: Dictionary = {}
var my_net_id: int = -1

var lobby_owner_cx_id: String = ""
var ping_data: Dictionary = {}       # region → ms, measured before lobby join
var game_start_time_ms: int = 0      # Unix epoch ms when match started (set by host, synced to non-hosts)

# 40-colour default palette, aligned with the js/dotnet/C#-Godot CursorParty clients.
# Overwritten at runtime from the "Colors" global property; seeding it here means the
# palette is never empty (no modulo-by-zero) and never short (all 40 swatches show)
# even if the property is undefined.
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
