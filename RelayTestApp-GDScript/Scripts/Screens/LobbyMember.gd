# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# One row in the lobby member list.
extends HBoxContainer

# Colour swatch — clickable ONLY on your own row (cpp: ColorButton opens a popup picker on
# your row only; react: canPickColor = isMe && teams.length === 1). Other members' swatches
# are display-only.
signal color_swatch_clicked

@onready var _color_button: Button  = $ColorButton
@onready var _color_rect:   ColorRect = $ColorButton/ColorRect
@onready var _name_label:  Label     = $NameLabel
@onready var _you_badge:   Label     = $YouBadge
@onready var _host_badge:  Label     = $HostBadge
@onready var _ready_label: Label    = $ReadyLabel
@onready var _ping_label:  Label     = $PingLabel
@onready var _rank_label:  Label     = $RankLabel

var cx_id: String = ""

func _ready() -> void:
	_color_button.pressed.connect(func(): color_swatch_clicked.emit())

func setup(member: Dictionary) -> void:
	cx_id = member.get("cxId", "")
	var is_host := not AppState.lobby_owner_cx_id.is_empty() and cx_id == AppState.lobby_owner_cx_id
	var is_me: bool = cx_id == AppState.user_cx_id
	_name_label.text = member.get("name", "Unknown")
	_host_badge.visible = is_host
	_you_badge.visible = is_me
	_color_button.disabled = not is_me
	_color_button.mouse_default_cursor_shape = Control.CURSOR_POINTING_HAND if is_me else Control.CURSOR_ARROW
	var extra: Dictionary = member.get("extra", {})
	set_color_index(int(extra.get("colorIndex", 0)))
	set_ready(member.get("isReady", false))
	# null (not just missing) can arrive from other clients' JSON.
	var rank = extra.get("rank", -1)
	set_rank(int(rank) if rank != null else -1)

	# Show the member's best reported region ping (from extra.pings shared via update_ready)
	var pings: Dictionary = extra.get("pings", {})
	if pings.is_empty():
		_ping_label.text = "-- ms"
	else:
		var best_ms := 9999
		for r: String in pings.keys():
			var ms := int(pings[r])
			if ms < best_ms:
				best_ms = ms
		set_ping(best_ms)

func set_color_index(index: int) -> void:
	if index < AppState.color_palette.size():
		_color_rect.color = AppState.color_palette[index]

func set_ready(is_ready: bool) -> void:
	_name_label.modulate.a = 1.0 if is_ready else 0.55
	_ready_label.text = "Ready" if is_ready else "Not Ready"
	_ready_label.add_theme_color_override("font_color", Color(0.35, 1.0, 0.45) if is_ready else Color(0.6, 0.6, 0.6))

func set_rank(rank: int) -> void:
	_rank_label.text = ("#%d" % rank) if rank >= 0 else "Unranked"
	_rank_label.add_theme_color_override("font_color", _rank_color(rank))

# Matches cpp rankColorFor: gold/silver/bronze, white otherwise; dim grey when unranked.
static func _rank_color(rank: int) -> Color:
	if rank < 0: return Color(0.6, 0.6, 0.6)
	if rank == 1: return Color(1.0, 0.84, 0.0)
	if rank == 2: return Color(0.75, 0.75, 0.75)
	if rank == 3: return Color(0.80, 0.50, 0.20)
	return Color.WHITE

func set_ping(ms: int) -> void:
	# Match CPP: a timed-out region (>=999) shows "T/O"; unmeasured shows "--".
	if ms < 0:
		_ping_label.text = "--"
	elif ms >= 999:
		_ping_label.text = "T/O"
	else:
		_ping_label.text = "%d ms" % ms
