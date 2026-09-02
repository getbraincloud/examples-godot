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

func set_ping(ms: int) -> void:
	# Match CPP: a timed-out region (>=999) shows "T/O"; unmeasured shows "--".
	if ms < 0:
		_ping_label.text = "--"
	elif ms >= 999:
		_ping_label.text = "T/O"
	else:
		_ping_label.text = "%d ms" % ms
