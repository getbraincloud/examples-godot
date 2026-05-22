# Copyright 2026 bitHeads, Inc. All Rights Reserved.
extends Control

signal end_match   # host End Match button OR auto-timeout
signal leave_game  # any player Leave Game button

const _CURSOR_SCENE    := preload("res://Scenes/Components/PlayerCursor.tscn")
const _SHOCKWAVE_SCENE := preload("res://Scenes/Components/Shockwave.tscn")
const _SPLOTCH_SCENE   := preload("res://Scenes/Components/Splotch.tscn")

const _GAME_W          := 800.0
const _GAME_H          := 600.0
const _MATCH_DURATION  := 90.0
const _COUNTDOWN_START := 80.0
const _MOVE_INTERVAL   := 0.05
const _PING_INTERVAL   := 2.0

@onready var _game_area:       Control       = %GameArea
@onready var _timer_label:     Label         = %TimerLabel
@onready var _countdown_label: Label         = %CountdownLabel
@onready var _player_panel:    VBoxContainer = %PlayerPanel
@onready var _move_timer:      Timer         = %MoveTimer
@onready var _host_controls:   VBoxContainer = %HostControls
@onready var _clear_btn:       Button        = %ClearSplotchesButton
@onready var _end_match_btn:   Button        = %EndMatchButton
@onready var _leave_btn:       Button        = %LeaveButton

# net_id → PlayerCursor node
var _cursors:      Dictionary = {}
# All active Splotch nodes (for clear_splotches)
var _splotches:    Array      = []
# cxId → ping Label
var _ping_labels:  Dictionary = {}
# bidirectional cx_id ↔ net_id
var _cx_to_net_id: Dictionary = {}
var _net_id_to_cx: Dictionary = {}
# Shockwave mask: cxId → bool (true = send to this player)
var _send_to:      Dictionary = {}

var _ping_timer: float = 0.0

func _ready() -> void:
	AppState.bc.relay_service.register_relay_callback(_on_relay_message)

	_move_timer.wait_time = _MOVE_INTERVAL
	_move_timer.timeout.connect(_send_position)
	_move_timer.start()

	_build_player_panel()
	_countdown_label.hide()
	_game_area.gui_input.connect(_on_game_area_input)

	if AppState.user_cx_id != "" and AppState.my_net_id >= 0:
		_cx_to_net_id[AppState.user_cx_id] = AppState.my_net_id
		_net_id_to_cx[AppState.my_net_id]  = AppState.user_cx_id

	var is_host := AppState.user_cx_id == AppState.lobby_owner_cx_id
	_host_controls.visible = is_host

	_clear_btn.pressed.connect(_on_clear_splotches_pressed)
	_end_match_btn.pressed.connect(_on_end_match_pressed)
	_leave_btn.pressed.connect(_on_leave_game_pressed)

# ── Tick ──────────────────────────────────────────────────────────────────────

func _process(delta: float) -> void:
	_ping_timer += delta

	if AppState.game_start_time_ms <= 0:
		return

	var elapsed   := float(int(Time.get_unix_time_from_system() * 1000.0) - AppState.game_start_time_ms) / 1000.0
	var remaining := maxf(_MATCH_DURATION - elapsed, 0.0)
	_timer_label.text = "%02d:%02d" % [int(remaining) / 60, int(remaining) % 60]

	if elapsed >= _COUNTDOWN_START and elapsed < _MATCH_DURATION:
		_countdown_label.show()
		_countdown_label.text = "%d" % (int(_MATCH_DURATION - elapsed) + 1)
	else:
		_countdown_label.hide()

	if elapsed >= _MATCH_DURATION and AppState.user_cx_id == AppState.lobby_owner_cx_id:
		AppState.game_start_time_ms = -1
		end_match.emit()

	if _ping_timer >= _PING_INTERVAL:
		_ping_timer = 0.0
		_broadcast_ping()
		_refresh_own_ping()

# ── Input ─────────────────────────────────────────────────────────────────────

func _on_game_area_input(event: InputEvent) -> void:
	if not (event is InputEventMouseButton):
		return
	var mb := event as InputEventMouseButton
	if mb.pressed and mb.button_index == MOUSE_BUTTON_LEFT:
		var local_pos := _game_area.get_local_mouse_position()
		if Rect2(Vector2.ZERO, Vector2(_GAME_W, _GAME_H)).has_point(local_pos):
			_do_shockwave(local_pos, AppState.my_color_index, true)

# ── Position broadcast ────────────────────────────────────────────────────────

func _send_position() -> void:
	var local_pos := _game_area.get_local_mouse_position()
	var norm_x    := clampf(local_pos.x / _GAME_W, 0.0, 1.0)
	var norm_y    := clampf(local_pos.y / _GAME_H, 0.0, 1.0)
	_send_relay_all({"op": "move", "data": {"x": norm_x, "y": norm_y}},
					false, true, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1)

# ── Ping broadcast ────────────────────────────────────────────────────────────

func _broadcast_ping() -> void:
	var ms: int = AppState.bc.relay_service.get_ping()
	if ms < 0:
		return
	_send_relay_all({"op": "relay_ping", "data": {"ping": ms}},
					false, false, BrainCloudRelay.CHANNEL_LOW_PRIORITY)

# ── Shockwave + splotch ───────────────────────────────────────────────────────

func _do_shockwave(pos: Vector2, color_index: int, broadcast: bool, seed: int = -1) -> void:
	var col := AppState.color_palette[color_index] if color_index < AppState.color_palette.size() else Color.WHITE

	# Generate seed when originating; remote calls pass the seed from the message.
	if seed < 0:
		seed = randi()

	var sw := _SHOCKWAVE_SCENE.instantiate() as Node2D
	sw.setup(col)
	sw.position = pos
	_game_area.add_child(sw)

	var sp := _SPLOTCH_SCENE.instantiate() as Node2D
	sp.setup(col, AppState.splotch_duration, seed)
	sp.position = pos
	_game_area.add_child(sp)
	_splotches.append(sp)

	if broadcast:
		var norm_x := pos.x / _GAME_W
		var norm_y := pos.y / _GAME_H
		var msg    := JSON.stringify({"op": "shockwave", "data": {"x": norm_x, "y": norm_y, "colorIndex": color_index, "seed": seed}}).to_utf8_buffer()
		_send_to_targets(msg, true, false, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1)

# ── Relay send helpers ────────────────────────────────────────────────────────

# Send to all players unconditionally.
func _send_relay_all(msg: Dictionary, reliable: bool, ordered: bool, channel: int) -> void:
	AppState.bc.relay_service.send(JSON.stringify(msg).to_utf8_buffer(),
								   BrainCloudRelay.TO_ALL_PLAYERS, reliable, ordered, channel)

# Send respecting the player mask checkboxes.
func _send_to_targets(data: PackedByteArray, reliable: bool, ordered: bool, channel: int) -> void:
	if _send_to.is_empty():
		AppState.bc.relay_service.send(data, BrainCloudRelay.TO_ALL_PLAYERS, reliable, ordered, channel)
		return
	var all_on := true
	for v in _send_to.values():
		if not v:
			all_on = false
			break
	if all_on:
		AppState.bc.relay_service.send(data, BrainCloudRelay.TO_ALL_PLAYERS, reliable, ordered, channel)
	else:
		for cx in _send_to:
			if _send_to[cx] and _cx_to_net_id.has(cx):
				AppState.bc.relay_service.send(data, _cx_to_net_id[cx], reliable, ordered, channel)

# ── Relay receive ─────────────────────────────────────────────────────────────

func _on_relay_message(net_id: int, raw: PackedByteArray) -> void:
	var msg = JSON.parse_string(raw.get_string_from_utf8())
	if not msg is Dictionary:
		return
	match msg.get("op", ""):
		"game_start":
			var start_time: int = int(msg.get("data", {}).get("startTime", 0))
			if start_time > 0 and AppState.game_start_time_ms == 0:
				AppState.game_start_time_ms = start_time
		"move":            _on_move(net_id, msg.get("data", {}))
		"shockwave":       _on_remote_shockwave(net_id, msg.get("data", {}))
		"relay_ping":      _on_remote_ping(net_id, int(msg.get("data", {}).get("ping", msg.get("ping", -1))))
		"clear_splotches": _clear_all_splotches()
		"end_match":       end_match.emit()

# ── Relay system messages (called by Main.gd) ─────────────────────────────────

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

# ── Relay message handlers ────────────────────────────────────────────────────

func _on_move(net_id: int, data: Dictionary) -> void:
	if net_id == AppState.my_net_id:
		return
	_get_or_create_cursor(net_id).move_to(float(data.get("x", 0.0)), float(data.get("y", 0.0)))

func _on_remote_shockwave(net_id: int, data: Dictionary) -> void:
	if net_id == AppState.my_net_id:
		return  # relay delivers to all including sender; skip own echo
	var cx: String = _net_id_to_cx.get(net_id, "")
	var member: Dictionary = _member_for_cx(cx)
	var color_index: int = member.get("extra", {}).get("colorIndex", int(data.get("colorIndex", 0)))
	var seed: int = int(data.get("seed", -1))
	var pos := Vector2(float(data.get("x", 0.0)) * _GAME_W,
					   float(data.get("y", 0.0)) * _GAME_H)
	_do_shockwave(pos, color_index, false, seed)

func _on_remote_ping(net_id: int, ms: int) -> void:
	var cx: String = _net_id_to_cx.get(net_id, "")
	if cx != "" and _ping_labels.has(cx):
		(_ping_labels[cx] as Label).text = ("-- ms" if ms < 0 else "%d ms" % ms)

# ── Clear splotches ───────────────────────────────────────────────────────────

func _clear_all_splotches() -> void:
	for sp in _splotches:
		if is_instance_valid(sp):
			sp.queue_free()
	_splotches.clear()

func _on_clear_splotches_pressed() -> void:
	_clear_all_splotches()
	_send_relay_all({"op": "clear_splotches"}, true, false, 0)

# ── Button handlers ───────────────────────────────────────────────────────────

func _on_end_match_pressed() -> void:
	end_match.emit()

func _on_leave_game_pressed() -> void:
	leave_game.emit()

# ── Player panel ──────────────────────────────────────────────────────────────

func _build_player_panel() -> void:
	for child in _player_panel.get_children():
		child.queue_free()
	_ping_labels.clear()
	_send_to.clear()

	for member: Dictionary in AppState.lobby_members:
		var cx:      String = member.get("cxId", "")
		var is_me:   bool   = cx == AppState.user_cx_id
		var is_host: bool   = cx == AppState.lobby_owner_cx_id

		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 4)

		# Color swatch
		var swatch := ColorRect.new()
		swatch.custom_minimum_size = Vector2(12, 12)
		swatch.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		var cidx: int = member.get("extra", {}).get("colorIndex", 0)
		if cidx < AppState.color_palette.size():
			swatch.color = AppState.color_palette[cidx]

		# Mask checkbox (hidden for own row — you always affect yourself locally)
		var cb := CheckBox.new()
		cb.button_pressed = true
		cb.custom_minimum_size = Vector2(20, 18)
		if is_me:
			cb.hide()
		else:
			_send_to[cx] = true
			cb.toggled.connect(func(v: bool): _send_to[cx] = v)

		# Name label with [H] and (me) badges
		var name_text :String = member.get("name", "?")
		if is_host: name_text += " [H]"
		if is_me:   name_text += " (me)"
		var name_lbl := Label.new()
		name_lbl.text = name_text
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_lbl.add_theme_font_size_override("font_size", 12)

		# Ping label
		var ping_lbl := Label.new()
		ping_lbl.text = "--"
		ping_lbl.add_theme_font_size_override("font_size", 11)
		ping_lbl.add_theme_color_override("font_color", Color(0.55, 0.55, 0.55))

		row.add_child(swatch)
		row.add_child(cb)
		row.add_child(name_lbl)
		row.add_child(ping_lbl)
		_player_panel.add_child(row)

		if cx != "":
			_ping_labels[cx] = ping_lbl

func _refresh_own_ping() -> void:
	var our_ping: int = AppState.bc.relay_service.get_ping()
	if _ping_labels.has(AppState.user_cx_id):
		(_ping_labels[AppState.user_cx_id] as Label).text = \
			("--" if our_ping < 0 else "%d ms" % our_ping)

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
	_game_area.add_child(cursor)  # must be before setup so @onready vars are valid
	cursor.setup(net_id, pname, cidx)
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
