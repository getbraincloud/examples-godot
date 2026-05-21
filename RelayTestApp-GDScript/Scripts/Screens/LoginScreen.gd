# Copyright 2026 bitHeads, Inc. All Rights Reserved.
extends Control

signal authenticated

const _SETTINGS_PATH := "user://settings.cfg"

@onready var _username_field: LineEdit = %UsernameField
@onready var _password_field: LineEdit = %PasswordField
@onready var _remember_check: CheckBox = %RememberCheck
@onready var _login_button: Button = %LoginButton
@onready var _status_label: Label = %StatusLabel

func _ready() -> void:
	_login_button.pressed.connect(_on_login_pressed)
	var auto_login := _load_saved_credentials()
	if auto_login:
		_try_reauthenticate()

func _try_reauthenticate() -> void:
	_set_busy(true)
	_status_label.text = "Reconnecting..."
	var response: Dictionary = await AppState.bc.reauthenticate()
	if response.get("status", 0) == 200:
		AppState.username = _username_field.text
		await _load_global_properties()
		authenticated.emit()
	else:
		_status_label.text = "Session expired — please log in."
		_set_busy(false)

func _on_login_pressed() -> void:
	var username := _username_field.text.strip_edges()
	var password := _password_field.text
	if username.is_empty() or password.is_empty():
		_status_label.text = "Enter username and password."
		return

	_set_busy(true)
	_status_label.text = "Logging in..."

	var response: Dictionary = await AppState.bc.authenticate_universal(username, password, true)
	if response.get("status", 0) == 200:
		AppState.username = username
		_save_credentials(username, password)
		await _load_global_properties()
		authenticated.emit()
	else:
		var reason: String = response.get("status_message", "Unknown error")
		_status_label.text = "Login failed: %s" % reason
		_set_busy(false)

func _load_global_properties() -> void:
	_status_label.text = "Loading app data..."
	var response: Dictionary = await AppState.bc.global_app_service.read_selected_properties(
		["Colours", "AllLobbyTypes", "SplotchDuration"]
	)
	if response.get("status", 0) != 200:
		return
	var data: Dictionary = response.get("data", {})

	# Colours — comma-separated hex values e.g. "ff0000,00ff00,..."
	var colours_csv: String = data.get("Colours", {}).get("value", "")
	AppState.color_palette.clear()
	for hex_val: String in colours_csv.split(","):
		var h := hex_val.strip_edges()
		if h.length() > 0:
			AppState.color_palette.append(Color("#" + h))

	# AllLobbyTypes — JSON-encoded object; each value is {"lobby": "TypeName", ...}
	# Mirrors C++ applyLobbyTypes(): parsed.isObject() → entry["lobby"] or entry (string)
	var lobby_types_json: String = data.get("AllLobbyTypes", {}).get("value", "")
	AppState.lobby_types.clear()
	var parsed_types = JSON.parse_string(lobby_types_json)
	if parsed_types is Dictionary:
		for key: String in parsed_types.keys():
			var entry = parsed_types[key]
			if entry is Dictionary and entry.has("lobby"):
				AppState.lobby_types.append(entry.get("lobby", ""))
			elif entry is String:
				AppState.lobby_types.append(entry)
	elif parsed_types is Array:
		for item in parsed_types:
			if item is Dictionary and item.has("lobby"):
				AppState.lobby_types.append(item.get("lobby", ""))
			elif item is String:
				AppState.lobby_types.append(item)
	if AppState.lobby_types.is_empty():
		AppState.lobby_types.append("CursorPartyv2")

	# SplotchDuration — integer seconds (-1 = forever)
	AppState.splotch_duration = int(data.get("SplotchDuration", {}).get("value", "-1"))

func _set_busy(busy: bool) -> void:
	_login_button.disabled = busy
	_username_field.editable = not busy
	_password_field.editable = not busy
	_remember_check.disabled = busy

func _load_saved_credentials() -> bool:
	var cfg := ConfigFile.new()
	if cfg.load(_SETTINGS_PATH) != OK:
		return false
	var remember: bool = cfg.get_value("auth", "remember_me", false)
	_remember_check.button_pressed = remember
	if remember:
		_username_field.text = cfg.get_value("auth", "username", "")
		_password_field.text = cfg.get_value("auth", "password", "")
	AppState.my_color_index      = cfg.get_value("prefs", "color_index", 0)
	AppState.selected_protocol   = cfg.get_value("prefs", "protocol", "WS")
	AppState.selected_lobby_type = cfg.get_value("prefs", "lobby_type", "")
	AppState.use_ping_data       = cfg.get_value("prefs", "use_ping_data", false)
	return remember and not _username_field.text.is_empty()

func _save_credentials(username: String, password: String) -> void:
	var cfg := ConfigFile.new()
	cfg.load(_SETTINGS_PATH)
	var remember := _remember_check.button_pressed
	cfg.set_value("auth", "remember_me", remember)
	cfg.set_value("auth", "username", username if remember else "")
	cfg.set_value("auth", "password", password if remember else "")
	cfg.set_value("prefs", "color_index", AppState.my_color_index)
	cfg.set_value("prefs", "protocol", AppState.selected_protocol)
	cfg.set_value("prefs", "lobby_type", AppState.selected_lobby_type)
	cfg.set_value("prefs", "use_ping_data", AppState.use_ping_data)
	cfg.save(_SETTINGS_PATH)
