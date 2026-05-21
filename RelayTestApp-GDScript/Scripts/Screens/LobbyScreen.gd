# Copyright 2026 bitHeads, Inc. All Rights Reserved.
extends Control

signal leave_lobby

const _MEMBER_SCENE := preload("res://Scenes/Screens/LobbyMember.tscn")

# Color picker grid constants
const _COLS         := 10
const _SWATCH_SIZE  := 28

@onready var _lobby_id_label:  Label         = %LobbyIdLabel
@onready var _region_label:    Label         = %RegionLabel
@onready var _player_count:    Label         = %PlayerCountLabel
@onready var _status_label:    Label         = %StatusLabel
@onready var _timer_label:     Label         = %TimerLabel
@onready var _member_list:     VBoxContainer = %MemberList
@onready var _color_grid:      GridContainer = %ColorGrid
@onready var _ready_button:    Button        = %ReadyButton
@onready var _leave_button:    Button        = %LeaveButton

var _is_ready:   bool  = false
var _elapsed:    float = 0.0

func _ready() -> void:
	_build_color_grid()
	_ready_button.pressed.connect(_on_ready_pressed)
	_leave_button.pressed.connect(_on_leave_pressed)

	# Populate initial members already in lobby (populated by RTT events before scene load)
	for m: Dictionary in AppState.lobby_members:
		_add_member_row(m)

	_refresh_info()

# ── Tick ─────────────────────────────────────────────────────────────────────

func _process(delta: float) -> void:
	_elapsed += delta
	var mins := int(_elapsed) / 60
	var secs := int(_elapsed) % 60
	_timer_label.text = "%02d:%02d" % [mins, secs]

# ── Color picker ──────────────────────────────────────────────────────────────

func _build_color_grid() -> void:
	_color_grid.columns = _COLS
	var palette := AppState.color_palette
	if palette.is_empty():
		# Fallback 8-colour palette
		palette = [Color.BLACK, Color.MAGENTA, Color.RED, Color.BLUE,
				   Color.CYAN, Color.GREEN, Color.YELLOW, Color.WHITE]

	for i in palette.size():
		var btn := ColorRect.new()
		btn.custom_minimum_size = Vector2(_SWATCH_SIZE, _SWATCH_SIZE)
		btn.color = palette[i]
		# Wrap in a Button so it's clickable
		var wrapper := Button.new()
		wrapper.custom_minimum_size = Vector2(_SWATCH_SIZE, _SWATCH_SIZE)
		wrapper.flat = true
		wrapper.add_child(btn)
		btn.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		var idx := i  # capture
		wrapper.pressed.connect(func(): _on_color_selected(idx))
		_color_grid.add_child(wrapper)

	_highlight_selected()

func _highlight_selected() -> void:
	for i in _color_grid.get_child_count():
		var w := _color_grid.get_child(i) as Button
		if w:
			w.modulate = Color(1.6, 1.6, 1.6) if i == AppState.my_color_index else Color.WHITE

func _on_color_selected(index: int) -> void:
	AppState.my_color_index = index
	_highlight_selected()
	# Persist and notify other lobby members
	var cfg := ConfigFile.new()
	cfg.load("user://settings.cfg")
	cfg.set_value("prefs", "color_index", index)
	cfg.save("user://settings.cfg")
	AppState.bc.lobby_service.update_ready(
		AppState.lobby_id, _is_ready,
		{"colorIndex": index}
	)

# ── Ready / leave ─────────────────────────────────────────────────────────────

func _on_ready_pressed() -> void:
	_is_ready = not _is_ready
	_ready_button.text = "Not Ready" if _is_ready else "Ready"
	AppState.bc.lobby_service.update_ready(
		AppState.lobby_id, _is_ready,
		{"colorIndex": AppState.my_color_index}
	)

func _on_leave_pressed() -> void:
	leave_lobby.emit()

# ── RTT event handler (called by Main.gd) ─────────────────────────────────────

func on_lobby_event(op: String, data: Dictionary) -> void:
	# AppState.lobby_members is already updated by Main.gd before this is called;
	# we only need to sync the UI rows here.
	match op:
		"MEMBER_JOIN":
			_add_member_row(data.get("member", {}))
		"MEMBER_LEFT":
			_remove_member_row(data.get("member", {}).get("cxId", ""))
		"MEMBER_UPDATE":
			_update_member_row(data.get("member", {}))
	_refresh_info()

# ── Member list helpers ───────────────────────────────────────────────────────

func _add_member_row(member: Dictionary) -> void:
	var row := _MEMBER_SCENE.instantiate() as HBoxContainer
	row.setup(member)
	_member_list.add_child(row)
	_refresh_info()

func _remove_member_row(cx: String) -> void:
	for child in _member_list.get_children():
		# Dynamic get avoids type error — cx_id is declared on LobbyMember, not on Node
		if child.get("cx_id") == cx:
			child.queue_free()
			break

func _update_member_row(member: Dictionary) -> void:
	var cx: String = member.get("cxId", "")
	for child in _member_list.get_children():
		if child.get("cx_id") == cx:
			child.setup(member)
			break

func _refresh_info() -> void:
	_lobby_id_label.text  = "Lobby: %s" % AppState.lobby_id
	_player_count.text    = "Players: %d" % _member_list.get_child_count()
	_region_label.text    = "Region: %s" % AppState.server_info.get("region", "—")
	_status_label.text    = "Waiting for players..."
