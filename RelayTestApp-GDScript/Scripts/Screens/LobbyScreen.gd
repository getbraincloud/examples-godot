# Copyright 2026 bitHeads, Inc. All Rights Reserved.
extends Control

signal leave_lobby
signal lobby_signal_send_requested(text: String)
signal global_chat_send_requested(text: String)

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
@onready var _color_popup:       PopupPanel    = %ColorPopup
@onready var _color_popup_grid:  GridContainer = %ColorPopupGrid
@onready var _ready_button:      Button        = %ReadyButton
@onready var _leave_button:      Button        = %LeaveButton
@onready var _lobby_chat_panel:  LobbySignalChatPanel = %LobbySignalChatPanel

# Right-side tabs: Chat / Leaderboards / Info (each panel is always instanced, only the
# selected one is visible — avoids the overlap that showing all three simultaneously caused).
@onready var _chat_tab_btn:        Button        = %ChatTabBtn
@onready var _leaderboards_tab_btn: Button       = %LeaderboardsTabBtn
@onready var _info_tab_btn:        Button        = %InfoTabBtn
@onready var _chat_tab:            Control       = %ChatTab
@onready var _leaderboards_tab:    Control       = %LeaderboardsTab
@onready var _info_tab:            Control       = %InfoTab

# Chat sub-tabs: This Lobby / Global.
@onready var _this_lobby_tab_btn: Button  = %ThisLobbyTabBtn
@onready var _global_tab_btn:     Button  = %GlobalTabBtn
@onready var _global_chat_panel:  GlobalChatPanel = %GlobalChatPanel

var _elapsed:       float  = 0.0
var _server_status: String = ""   # non-empty overrides the default status text

func _ready() -> void:
	_build_color_popup()
	_ready_button.pressed.connect(_on_ready_pressed)
	_leave_button.pressed.connect(_on_leave_pressed)
	_lobby_chat_panel.send_requested.connect(func(text): lobby_signal_send_requested.emit(text))
	set_lobby_chat_history(AppState.lobby_chat_history)

	_global_chat_panel.send_requested.connect(func(text): global_chat_send_requested.emit(text))
	_global_chat_panel.set_history(AppState.global_chat_history)

	_chat_tab_btn.pressed.connect(func(): _set_right_tab("chat"))
	_leaderboards_tab_btn.pressed.connect(func(): _set_right_tab("leaderboards"))
	_info_tab_btn.pressed.connect(func(): _set_right_tab("info"))
	_this_lobby_tab_btn.pressed.connect(func(): _set_chat_sub_tab("this_lobby"))
	_global_tab_btn.pressed.connect(func(): _set_chat_sub_tab("global"))
	_set_right_tab("chat")
	_set_chat_sub_tab("this_lobby")

	for m: Dictionary in AppState.lobby_members:
		_add_member_row(m)

	_refresh_info()
	_apply_role()

func _set_right_tab(tab: String) -> void:
	_chat_tab.visible = tab == "chat"
	_leaderboards_tab.visible = tab == "leaderboards"
	_info_tab.visible = tab == "info"
	_chat_tab_btn.modulate = Color(1.4, 1.4, 1.4) if tab == "chat" else Color.WHITE
	_leaderboards_tab_btn.modulate = Color(1.4, 1.4, 1.4) if tab == "leaderboards" else Color.WHITE
	_info_tab_btn.modulate = Color(1.4, 1.4, 1.4) if tab == "info" else Color.WHITE

func _set_chat_sub_tab(tab: String) -> void:
	_lobby_chat_panel.visible = tab == "this_lobby"
	_global_chat_panel.visible = tab == "global"
	_this_lobby_tab_btn.modulate = Color(1.4, 1.4, 1.4) if tab == "this_lobby" else Color.WHITE
	_global_tab_btn.modulate = Color(1.4, 1.4, 1.4) if tab == "global" else Color.WHITE

# Called once by Main right after this screen is created, to replay any this-lobby chat
# history that was sent before this screen instance existed (e.g. from a prior round).
func set_lobby_chat_history(history: Array) -> void:
	_lobby_chat_panel.clear()
	for row: Dictionary in history:
		_lobby_chat_panel.add_message(row.get("from", "Player"), row.get("text", ""), row.get("is_me", false))

func add_lobby_chat_message(from_name: String, text: String, is_me: bool) -> void:
	_lobby_chat_panel.add_message(from_name, text, is_me)

func add_global_chat_message(msg_id: String, from_name: String, text: String) -> void:
	_global_chat_panel.add_message(msg_id, from_name, text)

func update_global_chat_message(msg_id: String, text: String) -> void:
	_global_chat_panel.update_message(msg_id, text)

func remove_global_chat_message(msg_id: String) -> void:
	_global_chat_panel.remove_message(msg_id)

# Non-blocking status override shown inline (used while a round is being provisioned —
# STARTING -> ROOM_READY -> relay connect — the rest of this screen, including chat, stays
# fully usable underneath it instead of being replaced by a blocking loading screen).
func set_status_override(text: String) -> void:
	_server_status = text
	_refresh_info()

func _apply_role() -> void:
	# Owner is unknown until the first MEMBER_JOIN carries the lobby object — guard against
	# both strings being empty, which would otherwise compare equal.
	var owner_known := not AppState.lobby_owner_cx_id.is_empty()
	var is_owner    := owner_known and AppState.user_cx_id == AppState.lobby_owner_cx_id
	if is_owner:
		_ready_button.text     = "Start"
		_ready_button.disabled = false
	elif owner_known:
		# Non-host auto-readies immediately so the host can start without waiting
		if not AppState.user_is_ready:
			AppState.user_is_ready = true
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
	_timer_label.text = "Time in lobby: %02d:%02d" % [mins, secs]

# ── Color picker ──────────────────────────────────────────────────────────────
# A small popup under your own row's swatch (cpp: ImGui popup opened from your row's
# ColorButton; react: colorPickerOpen toggled from your row, canPickColor = isMe) — not an
# always-visible palette grid.

func _build_color_popup() -> void:
	_color_popup_grid.columns = _COLS
	var palette := AppState.color_palette
	if palette.is_empty():
		palette = AppState.default_palette()

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
		_color_popup_grid.add_child(wrapper)

	_highlight_selected()

func _highlight_selected() -> void:
	for i in _color_popup_grid.get_child_count():
		var w := _color_popup_grid.get_child(i) as Button
		if w:
			w.modulate = Color(1.6, 1.6, 1.6) if i == AppState.my_color_index else Color.WHITE

# Called by a member row's color_swatch_clicked signal (own row only — LobbyMember gates this).
func _on_color_swatch_clicked(row: Control) -> void:
	var pos := row.get_global_rect().position + Vector2(0, row.get_global_rect().size.y)
	_color_popup.position = Vector2i(pos)
	_color_popup.popup()

func _on_color_selected(index: int) -> void:
	AppState.my_color_index = index
	_highlight_selected()
	_color_popup.hide()

	# Instant local feedback — the server echo (MEMBER_UPDATE) will confirm shortly after.
	_update_member_row({"cxId": AppState.user_cx_id, "name": AppState.username, "isReady": AppState.user_is_ready,
		"extra": {"colorIndex": index, "pings": AppState.ping_data}})

	var cfg := ConfigFile.new()
	cfg.load("user://settings.cfg")
	cfg.set_value("prefs", "color_index", index)
	cfg.save("user://settings.cfg")
	AppState.bc.lobby_service.update_ready(
		AppState.lobby_id, AppState.user_is_ready,
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
		AppState.user_is_ready = not AppState.user_is_ready
		_ready_button.text = "Not Ready" if AppState.user_is_ready else "Ready"
		AppState.bc.lobby_service.update_ready(
			AppState.lobby_id, AppState.user_is_ready,
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
		"ROOM_PROGRESS":
			var cur_step: int = int(data.get("curStep", 0))
			var of_step: int = int(data.get("ofStep", 0))
			var msg: String = str(data.get("msg", ""))
			_server_status = ("%d/%d: %s" % [cur_step, of_step, msg]) if cur_step > 0 else msg
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
	row.color_swatch_clicked.connect(func(): _on_color_swatch_clicked(row))
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
	# Also seed from our own captured pings so our columns appear before the lobby echoes
	# our extra["pings"] back to us (mirrors CPP seeding the region union from local data).
	for r: String in AppState.ping_data.keys():
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
		# Tint each member's name by their chosen palette colour, so the lobby ping table matches
		# the C#/CPP clients (which colour the member name) — visual parity across clients.
		var cidx: int = int(m.get("extra", {}).get("colorIndex", 0))
		if cidx >= 0 and cidx < AppState.color_palette.size():
			name_lbl.add_theme_color_override("font_color", AppState.color_palette[cidx])
		member_row.add_child(name_lbl)
		var pings: Dictionary = m.get("extra", {}).get("pings", {})
		# Our own row may render before the lobby echoes our extra["pings"] back — fall back to
		# the locally captured ping data so our cells aren't blank (mirrors CPP self-fallback).
		if pings.is_empty() and m.get("cxId", "") == AppState.user_cx_id:
			pings = AppState.ping_data
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
				# >=999 is a timeout — show CPP's "T/O" instead of a raw number.
				ping_lbl.text = "T/O" if ms >= 999 else "%d ms" % ms
				ping_lbl.add_theme_color_override("font_color", Color(1.0, 0.4, 0.4))
			member_row.add_child(ping_lbl)
		_ping_regions.add_child(member_row)


static func _region_from_lobby_id(id: String) -> String:
	var pos := id.find(":")
	return id.substr(0, pos) if pos > 0 else ""
