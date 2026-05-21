# Copyright 2026 bitHeads, Inc. All Rights Reserved.
extends Control

signal end_match  # emitted by host only

const _CURSOR_SCENE    := preload("res://Scenes/Components/PlayerCursor.tscn")
const _SHOCKWAVE_SCENE := preload("res://Scenes/Components/Shockwave.tscn")
const _SPLOTCH_SCENE   := preload("res://Scenes/Components/Splotch.tscn")

const _GAME_W          := 800.0
const _GAME_H          := 600.0
const _MATCH_DURATION  := 90.0   # seconds
const _COUNTDOWN_START := 80.0   # start 10-second countdown at this point
const _MOVE_INTERVAL   := 0.05   # position broadcast ~20 fps
const _PING_INTERVAL   := 2.0    # ping broadcast every 2 s

@onready var _game_area:       Control      = %GameArea
@onready var _timer_label:     Label        = %TimerLabel
@onready var _countdown_label: Label        = %CountdownLabel
@onready var _player_panel:    VBoxContainer = %PlayerPanel
@onready var _move_timer:      Timer        = %MoveTimer

# net_id → PlayerCursor node
var _cursors:      Dictionary = {}
# cxId → ping Label node (keyed by string for reliable lookups)
var _ping_labels:  Dictionary = {}
# bidirectional cxId ↔ net_id map, built from relay CONNECT messages
var _cx_to_net_id: Dictionary = {}
var _net_id_to_cx: Dictionary = {}

var _elapsed:    float = 0.0
var _ping_timer: float = 0.0

func _ready() -> void:
	AppState.bc.relay_service.register_relay_callback(_on_relay_message)
	_move_timer.wait_time = _MOVE_INTERVAL
	_move_timer.timeout.connect(_send_position)
	_move_timer.start()
	_build_player_panel()
	_countdown_label.hide()
	# GameArea has mouse_filter=Stop so it captures clicks; route to our handler.
	_game_area.gui_input.connect(_on_game_area_input)
	# Seed our own cxId↔netId mapping (both are known at game start)
	if AppState.user_cx_id != "" and AppState.my_net_id >= 0:
		_cx_to_net_id[AppState.user_cx_id] = AppState.my_net_id
		_net_id_to_cx[AppState.my_net_id]  = AppState.user_cx_id

# ── Input ─────────────────────────────────────────────────────────────────────

func _on_game_area_input(event: InputEvent) -> void:
	if not (event is InputEventMouseButton):
		return
	var mb := event as InputEventMouseButton
	if mb.pressed and mb.button_index == MOUSE_BUTTON_LEFT:
		var local_pos := _game_area.get_local_mouse_position()
		if Rect2(Vector2.ZERO, Vector2(_GAME_W, _GAME_H)).has_point(local_pos):
			_do_shockwave(local_pos, AppState.my_color_index, true)

# ── Tick ─────────────────────────────────────────────────────────────────────

func _process(delta: float) -> void:
	_elapsed    += delta
	_ping_timer += delta

	# Match timer display
	var remaining := maxf(_MATCH_DURATION - _elapsed, 0.0)
	var mins      := int(remaining) / 60
	var secs      := int(remaining) % 60
	_timer_label.text = "%02d:%02d" % [mins, secs]

	# 10-second countdown
	if _elapsed >= _COUNTDOWN_START and _elapsed < _MATCH_DURATION:
		_countdown_label.show()
		_countdown_label.text = "%d" % (int(_MATCH_DURATION - _elapsed) + 1)
	else:
		_countdown_label.hide()

	# Host ends the match at time limit
	if _elapsed >= _MATCH_DURATION and AppState.my_net_id == 0:
		_elapsed = -999.0  # prevent re-trigger
		end_match.emit()

	# Broadcast our ping and refresh the HUD every _PING_INTERVAL seconds
	if _ping_timer >= _PING_INTERVAL:
		_ping_timer = 0.0
		_broadcast_ping()
		_refresh_own_ping()

# ── Position broadcast ────────────────────────────────────────────────────────

func _send_position() -> void:
	var local_pos := _game_area.get_local_mouse_position()
	var norm_x    := clampf(local_pos.x / _GAME_W, 0.0, 1.0)
	var norm_y    := clampf(local_pos.y / _GAME_H, 0.0, 1.0)
	_send_relay({"op": "move", "data": {"x": norm_x, "y": norm_y}},
				false, true, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1)

# ── Ping broadcast ────────────────────────────────────────────────────────────

func _broadcast_ping() -> void:
	var ms: int = AppState.bc.relay_service.get_ping()
	if ms < 0:
		return
	_send_relay({"op": "relay_ping", "ping": ms},
				false, false, BrainCloudRelay.CHANNEL_LOW_PRIORITY)

# ── Shockwave + splotch ───────────────────────────────────────────────────────

func _do_shockwave(pos: Vector2, color_index: int, broadcast: bool) -> void:
	var col := AppState.color_palette[color_index] if color_index < AppState.color_palette.size() else Color.WHITE

	var sw := _SHOCKWAVE_SCENE.instantiate() as Node2D
	sw.setup(col)
	sw.position = pos
	_game_area.add_child(sw)

	var sp := _SPLOTCH_SCENE.instantiate() as Node2D
	sp.setup(col, AppState.splotch_duration)
	sp.position = pos
	_game_area.add_child(sp)

	if broadcast:
		var norm_x := pos.x / _GAME_W
		var norm_y := pos.y / _GAME_H
		_send_relay({"op": "shockwave", "data": {"x": norm_x, "y": norm_y, "colorIndex": color_index}},
					true, false, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_2)

# ── Relay send helper ─────────────────────────────────────────────────────────

func _send_relay(msg: Dictionary, reliable: bool, ordered: bool, channel: int) -> void:
	var bytes := JSON.stringify(msg).to_utf8_buffer()
	AppState.bc.relay_service.send(bytes, BrainCloudRelay.TO_ALL_PLAYERS, reliable, ordered, channel)

# ── Relay message handler ─────────────────────────────────────────────────────

func _on_relay_message(net_id: int, raw: PackedByteArray) -> void:
	var json_str := raw.get_string_from_utf8()
	var msg = JSON.parse_string(json_str)
	if not msg is Dictionary:
		return
	match msg.get("op", ""):
		"move":       _on_move(net_id, msg.get("data", {}))
		"shockwave":  _on_remote_shockwave(net_id, msg.get("data", {}))
		"relay_ping": _on_remote_ping(net_id, int(msg.get("ping", -1)))

func _on_move(net_id: int, data: Dictionary) -> void:
	if net_id == AppState.my_net_id:
		return
	var cursor := _get_or_create_cursor(net_id)
	cursor.move_to(float(data.get("x", 0.0)), float(data.get("y", 0.0)))

func _on_remote_shockwave(net_id: int, data: Dictionary) -> void:
	var color_index: int = int(data.get("colorIndex", 0))
	var pos := Vector2(float(data.get("x", 0.0)) * _GAME_W,
					   float(data.get("y", 0.0)) * _GAME_H)
	_do_shockwave(pos, color_index, false)

func _on_remote_ping(net_id: int, ms: int) -> void:
	var cx: String = _net_id_to_cx.get(net_id, "")
	if cx != "" and _ping_labels.has(cx):
		var lbl := _ping_labels[cx] as Label
		lbl.text = ("-- ms" if ms < 0 else "%d ms" % ms)

# ── System messages (called by Main.gd) ──────────────────────────────────────

func on_relay_system(op: String, msg: Dictionary) -> void:
	match op:
		"CONNECT":
			var cx: String = msg.get("cxId", "")
			var nid: int   = msg.get("netId", -1)
			if cx != "" and nid >= 0:
				_cx_to_net_id[cx]  = nid
				_net_id_to_cx[nid] = cx
		"DISCONNECT":
			var cx: String = msg.get("cxId", "")
			_remove_cursor_for_cx(cx)

# ── Cursor management ─────────────────────────────────────────────────────────

func _get_or_create_cursor(net_id: int) -> Node2D:
	if _cursors.has(net_id):
		return _cursors[net_id]

	var cx:     String     = _net_id_to_cx.get(net_id, "")
	var member: Dictionary = _member_for_cx(cx)
	var cursor             = _CURSOR_SCENE.instantiate()
	var pname:  String     = member.get("name", "Player %d" % net_id)
	var extra:  Dictionary = member.get("extra", {})
	var cidx:   int        = extra.get("colorIndex", net_id % AppState.color_palette.size())
	cursor.setup(net_id, pname, cidx)
	_game_area.add_child(cursor)
	_cursors[net_id] = cursor
	return cursor

func _remove_cursor_for_cx(cx: String) -> void:
	if not _cx_to_net_id.has(cx):
		return
	var nid: int = _cx_to_net_id[cx]
	if _cursors.has(nid):
		_cursors[nid].queue_free()
		_cursors.erase(nid)
	_net_id_to_cx.erase(nid)
	_cx_to_net_id.erase(cx)

func _member_for_cx(cx: String) -> Dictionary:
	for m: Dictionary in AppState.lobby_members:
		if m.get("cxId", "") == cx:
			return m
	return {}

# ── HUD player panel ──────────────────────────────────────────────────────────

func _build_player_panel() -> void:
	for child in _player_panel.get_children():
		child.queue_free()
	_ping_labels.clear()

	for member: Dictionary in AppState.lobby_members:
		var row    := HBoxContainer.new()
		var swatch := ColorRect.new()
		swatch.custom_minimum_size = Vector2(14, 14)
		swatch.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		var cidx: int = member.get("extra", {}).get("colorIndex", 0)
		if cidx < AppState.color_palette.size():
			swatch.color = AppState.color_palette[cidx]
		var name_lbl := Label.new()
		name_lbl.text = member.get("name", "?")
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_lbl.add_theme_font_size_override("font_size", 13)
		var ping_lbl := Label.new()
		ping_lbl.text = "-- ms"
		ping_lbl.add_theme_font_size_override("font_size", 12)
		ping_lbl.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))
		row.add_child(swatch)
		row.add_child(name_lbl)
		row.add_child(ping_lbl)
		_player_panel.add_child(row)
		# Key ping label by cxId for direct relay-event lookup
		var cx: String = member.get("cxId", "")
		if cx != "":
			_ping_labels[cx] = ping_lbl

func _refresh_own_ping() -> void:
	var our_ping: int = AppState.bc.relay_service.get_ping()
	if _ping_labels.has(AppState.user_cx_id):
		var lbl := _ping_labels[AppState.user_cx_id] as Label
		lbl.text = ("-- ms" if our_ping < 0 else "%d ms" % our_ping)
