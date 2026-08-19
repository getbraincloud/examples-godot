# Copyright 2026 bitHeads, Inc. All Rights Reserved.
extends Control

signal matchmake_requested(lobby_type: String, protocol: String, use_ping: bool)
signal global_chat_send_requested(text: String)

const _SETTINGS_PATH := "user://settings.cfg"
const _PROTOCOLS     := ["WS", "WSS", "UDP", "TCP"]

@onready var _lobby_type_option: OptionButton = %LobbyTypeOption
@onready var _protocol_option:   OptionButton = %ProtocolOption
@onready var _ping_check:        CheckBox     = %PingDataCheck
@onready var _find_button:       Button       = %FindButton
@onready var _status_label:      Label        = %StatusLabel

@onready var _leaderboard_tab_btn: Button = %LeaderboardTabBtn
@onready var _chat_tab_btn:        Button = %ChatTabBtn
@onready var _leaderboard_panel:   Control = %LeaderboardPanel
@onready var _chat_panel:          GlobalChatPanel = %GlobalChatPanel

func _ready() -> void:
	_populate_lobby_types()
	_populate_protocols()
	_load_saved_prefs()
	_find_button.pressed.connect(_on_find_pressed)

	_leaderboard_tab_btn.pressed.connect(func(): _set_right_tab("leaderboard"))
	_chat_tab_btn.pressed.connect(func(): _set_right_tab("chat"))
	_set_right_tab("leaderboard")

	_chat_panel.send_requested.connect(func(text): global_chat_send_requested.emit(text))
	_chat_panel.set_history(AppState.global_chat_history)

func _set_right_tab(tab: String) -> void:
	_leaderboard_panel.visible = tab == "leaderboard"
	_chat_panel.visible = tab == "chat"
	_leaderboard_tab_btn.modulate = Color(1.4, 1.4, 1.4) if tab == "leaderboard" else Color.WHITE
	_chat_tab_btn.modulate = Color(1.4, 1.4, 1.4) if tab == "chat" else Color.WHITE

func add_global_chat_message(msg_id: String, from_name: String, text: String) -> void:
	_chat_panel.add_message(msg_id, from_name, text)

func update_global_chat_message(msg_id: String, text: String) -> void:
	_chat_panel.update_message(msg_id, text)

func remove_global_chat_message(msg_id: String) -> void:
	_chat_panel.remove_message(msg_id)

func _populate_lobby_types() -> void:
	_lobby_type_option.clear()
	if AppState.lobby_types.is_empty():
		_lobby_type_option.add_item("CursorParty")
	else:
		for t: String in AppState.lobby_types:
			_lobby_type_option.add_item(t)

func _populate_protocols() -> void:
	_protocol_option.clear()
	for p: String in _PROTOCOLS:
		_protocol_option.add_item(p)

func _load_saved_prefs() -> void:
	# Select saved lobby type
	var saved_type := AppState.selected_lobby_type
	for i in _lobby_type_option.item_count:
		if _lobby_type_option.get_item_text(i) == saved_type:
			_lobby_type_option.select(i)
			break

	# Select saved protocol, unless a ?protocol= URL override (e.g. "wss" for testing)
	# names a valid one — matched case-insensitively and takes priority over the saved pref.
	var saved_protocol := AppState.selected_protocol
	var url_protocol := AppState.get_url_protocol().to_upper()
	if not url_protocol.is_empty() and _PROTOCOLS.has(url_protocol):
		saved_protocol = url_protocol
	var proto_idx := _PROTOCOLS.find(saved_protocol)
	_protocol_option.select(max(proto_idx, 0))

	_ping_check.button_pressed = AppState.use_ping_data

func _on_find_pressed() -> void:
	var lobby_type := _lobby_type_option.get_item_text(_lobby_type_option.selected)
	var protocol   := _protocol_option.get_item_text(_protocol_option.selected)
	var use_ping   := _ping_check.button_pressed

	# Persist choices
	AppState.selected_lobby_type = lobby_type
	AppState.selected_protocol   = protocol
	AppState.use_ping_data       = use_ping
	_save_prefs()

	_find_button.disabled = true
	_status_label.text    = "Searching..."
	matchmake_requested.emit(lobby_type, protocol, use_ping)

func _save_prefs() -> void:
	var cfg := ConfigFile.new()
	cfg.load(_SETTINGS_PATH)
	cfg.set_value("prefs", "lobby_type",    AppState.selected_lobby_type)
	cfg.set_value("prefs", "protocol",      AppState.selected_protocol)
	cfg.set_value("prefs", "use_ping_data", AppState.use_ping_data)
	cfg.save(_SETTINGS_PATH)
