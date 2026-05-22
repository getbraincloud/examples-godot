# Copyright 2026 bitHeads, Inc. All Rights Reserved.
extends Control

signal leave_lobby

const _MEMBER_SCENE := preload("res://Scenes/Screens/LobbyMember.tscn")

# Color picker grid constants
const _COLS         := 10
const _SWATCH_SIZE  := 28

@onready var _lobby_id_label:    Label         = %LobbyIdLabel
@onready var _region_label:      Label         = %RegionLabel
@onready var _ping_regions:      VBoxContainer = %PingRegionsContainer
@onready var _player_count:      Label         = %PlayerCountLabel
@onready var _status_label:      Label         = %StatusLabel
@onready var _timer_label:       Label         = %TimerLabel
@onready var _member_list:       VBoxContainer = %MemberList
@onready var _color_grid:        GridContainer = %ColorGrid
@onready var _ready_button:      Button        = %ReadyButton
@onready var _leave_button:      Button        = %LeaveButton

var _is_ready:      bool   = false
var _elapsed:       float  = 0.0
var _server_status: String = ""   # non-empty overrides the default status text

func _ready() -> void:
	_build_color_grid()
	_ready_button.pressed.connect(_on_ready_pressed)
	_leave_button.pressed.connect(_on_leave_pressed)

	for m: Dictionary in AppState.lobby_members:
		_add_member_row(m)

	_refresh_info()
	_apply_role()

func _apply_role() -> void:
	# Guard: owner is unknown until the first MEMBER_JOIN carries the lobby object.
	# If both strings are empty the comparison would incorrectly return true.
	var owner_known := not AppState.lobby_owner_cx_id.is_empty()
	var is_owner    := owner_known and AppState.user_cx_id == AppState.lobby_owner_cx_id
	if is_owner:
		_ready_button.text     = "Start"
		_ready_button.disabled = false
	elif owner_known:
		# Non-host auto-readies immediately so the host can start without waiting
		if not _is_ready:
			_is_ready = true
			AppState.bc.lobby_service.update_ready(
				AppState.lobby_id, true,
				{"colorIndex": AppState.my_color_index, "pings": AppState.ping_data}
			)
		_ready_button.text = "Not Ready"

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
		palette = [Color.BLACK, Color.MAGENTA, Color.RED, Color.BLUE,
				   Color.CYAN, Color.GREEN, Color.YELLOW, Color.WHITE]

	for i in palette.size():
		var btn := ColorRect.new()
		btn.custom_minimum_size = Vector2(_SWATCH_SIZE, _SWATCH_SIZE)
		btn.color = palette[i]
		btn.mouse_filter = Control.MOUSE_FILTER_IGNORE  # let clicks reach the Button
		var wrapper := Button.new()
		wrapper.custom_minimum_size = Vector2(_SWATCH_SIZE, _SWATCH_SIZE)
		wrapper.flat = true
		wrapper.add_child(btn)
		btn.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
		var idx := i
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

	var cfg := ConfigFile.new()
	cfg.load("user://settings.cfg")
	cfg.set_value("prefs", "color_index", index)
	cfg.save("user://settings.cfg")
	AppState.bc.lobby_service.update_ready(
		AppState.lobby_id, _is_ready,
		{"colorIndex": index, "pings": AppState.ping_data}
	)

# ── Ready / Start / Leave ─────────────────────────────────────────────────────

func _on_ready_pressed() -> void:
	var owner_known := not AppState.lobby_owner_cx_id.is_empty()
	var is_owner    := owner_known and AppState.user_cx_id == AppState.lobby_owner_cx_id
	if is_owner:
		# Host pressing Start — mark ready to trigger match launch
		_ready_button.disabled = true
		_status_label.text = "Starting..."
		AppState.bc.lobby_service.update_ready(
			AppState.lobby_id, true,
			{"colorIndex": AppState.my_color_index, "pings": AppState.ping_data}
		)
	else:
		_is_ready = not _is_ready
		_ready_button.text = "Not Ready" if _is_ready else "Ready"
		AppState.bc.lobby_service.update_ready(
			AppState.lobby_id, _is_ready,
			{"colorIndex": AppState.my_color_index, "pings": AppState.ping_data}
		)

func _on_leave_pressed() -> void:
	leave_lobby.emit()

# ── RTT event handler (called by Main.gd) ─────────────────────────────────────

func on_lobby_event(op: String, data: Dictionary) -> void:
	match op:
		"MEMBER_JOIN":
			# AppState.lobby_members is already rebuilt from lobby.members by Main.gd;
			# rebuild the UI list to match exactly so rows are never out of sync.
			_rebuild_member_list(AppState.lobby_members)
			# Owner may have just been set for the first time — re-evaluate our role.
			_apply_role()
		"MEMBER_LEFT":
			_remove_member_row(data.get("member", {}).get("cxId", ""))
		"MEMBER_UPDATE":
			_update_member_row(data.get("member", {}))
		"ROOM_UPDATE":
			var lobby_obj: Dictionary = data.get("lobby", {})
			if not lobby_obj.is_empty():
				_rebuild_member_list(lobby_obj.get("members", []))
			_apply_role()
		"STARTING":
			_server_status = "Server is starting up..."
			_ready_button.disabled = true
		"ROOM_ASSIGNED":
			_server_status = "Server assigned — waiting for relay..."
			_ready_button.disabled = true
		"ROOM_READY":
			_server_status = "Connecting to server..."
			_ready_button.disabled = true
	_refresh_info()

# ── Member list helpers ───────────────────────────────────────────────────────

func _add_member_row(member: Dictionary) -> void:
	var row := _MEMBER_SCENE.instantiate() as HBoxContainer
	_member_list.add_child(row)  # must be before setup so @onready vars are valid
	row.setup(member)
	_refresh_info()

func _remove_member_row(cx: String) -> void:
	for child in _member_list.get_children():
		if child.get("cx_id") == cx:
			child.queue_free()
			break

func _update_member_row(member: Dictionary) -> void:
	var cx: String = member.get("cxId", "")
	for child in _member_list.get_children():
		if child.get("cx_id") == cx:
			child.setup(member)
			break

func _rebuild_member_list(members: Array) -> void:
	for child in _member_list.get_children():
		child.queue_free()
	for m in members:
		if m is Dictionary:
			_add_member_row(m)

# ── Info bar ──────────────────────────────────────────────────────────────────

func _refresh_info() -> void:
	_lobby_id_label.text = "Lobby: %s" % AppState.lobby_id
	_player_count.text   = "Players: %d" % _member_list.get_child_count()

	# Region: extract prefix from lobby ID (format "region:LobbyType:N")
	var region := _region_from_lobby_id(AppState.lobby_id)
	var ping_ms := AppState.ping_data.get(region, -1) as int
	if region.is_empty():
		_region_label.text = "Region: —"
	elif ping_ms >= 0:
		_region_label.text = "Region: %s  (%d ms)" % [region, ping_ms]
	else:
		_region_label.text = "Region: %s" % region

	var owner_known := not AppState.lobby_owner_cx_id.is_empty()
	var is_owner    := owner_known and AppState.user_cx_id == AppState.lobby_owner_cx_id
	if not _server_status.is_empty():
		_status_label.text = _server_status
	elif is_owner:
		_status_label.text = "Press Start when ready."
	else:
		_status_label.text = "Waiting for players..."

	# Per-member ping table (shown when any member has reported ping data)
	for child in _ping_regions.get_children():
		child.queue_free()

	var all_regions: Array[String] = []
	for m: Dictionary in AppState.lobby_members:
		for r: String in (m.get("extra", {}).get("pings", {}) as Dictionary).keys():
			if not all_regions.has(r):
				all_regions.append(r)
	all_regions.sort()

	if all_regions.is_empty():
		return

	var sep2 := HSeparator.new()
	_ping_regions.add_child(sep2)

	var title := Label.new()
	title.add_theme_font_size_override("font_size", 13)
	title.text = "Ping Data (ms)"
	_ping_regions.add_child(title)

	# Header row
	var header_row := HBoxContainer.new()
	header_row.add_theme_constant_override("separation", 8)
	var header_name := Label.new()
	header_name.add_theme_font_size_override("font_size", 11)
	header_name.custom_minimum_size = Vector2(120, 0)
	header_row.add_child(header_name)
	for r: String in all_regions:
		var lbl := Label.new()
		lbl.add_theme_font_size_override("font_size", 11)
		lbl.add_theme_color_override("font_color", Color(0.7, 0.7, 0.7))
		lbl.custom_minimum_size = Vector2(80, 0)
		lbl.text = r
		header_row.add_child(lbl)
	_ping_regions.add_child(header_row)

	# One row per member
	for m: Dictionary in AppState.lobby_members:
		var is_host: bool = owner_known and m.get("cxId", "") == AppState.lobby_owner_cx_id
		var member_row := HBoxContainer.new()
		member_row.add_theme_constant_override("separation", 8)
		var name_lbl := Label.new()
		name_lbl.add_theme_font_size_override("font_size", 11)
		name_lbl.custom_minimum_size = Vector2(120, 0)
		name_lbl.text = "%s%s" % [m.get("name", "?"), " [Host]" if is_host else ""]
		member_row.add_child(name_lbl)
		var pings: Dictionary = m.get("extra", {}).get("pings", {})
		for r: String in all_regions:
			var ms: int = int(pings.get(r, -1))
			var ping_lbl := Label.new()
			ping_lbl.add_theme_font_size_override("font_size", 11)
			ping_lbl.custom_minimum_size = Vector2(80, 0)
			if ms < 0:
				ping_lbl.text = "—"
				ping_lbl.add_theme_color_override("font_color", Color(0.5, 0.5, 0.5))
			elif ms < 100:
				ping_lbl.text = "%d ms" % ms
				ping_lbl.add_theme_color_override("font_color", Color(0.3, 1.0, 0.4))
			elif ms < 250:
				ping_lbl.text = "%d ms" % ms
				ping_lbl.add_theme_color_override("font_color", Color(1.0, 1.0, 0.3))
			else:
				ping_lbl.text = "%d ms" % ms
				ping_lbl.add_theme_color_override("font_color", Color(1.0, 0.4, 0.4))
			member_row.add_child(ping_lbl)
		_ping_regions.add_child(member_row)


static func _region_from_lobby_id(id: String) -> String:
	var pos := id.find(":")
	return id.substr(0, pos) if pos > 0 else ""
