# Copyright 2026 bitHeads, Inc. All Rights Reserved.
extends Control

const _LOGIN_SCENE     := preload("res://Scenes/Screens/LoginScreen.tscn")
const _MENU_SCENE      := preload("res://Scenes/Screens/MainMenuScreen.tscn")
const _LOBBY_SCENE     := preload("res://Scenes/Screens/LobbyScreen.tscn")
const _GAME_SCENE      := preload("res://Scenes/Screens/GameScreen.tscn")

@onready var _screen_container: Control = %ScreenContainer
@onready var _version_label:    Label   = %VersionLabel
@onready var _loading_overlay:  Control = %LoadingOverlay
@onready var _loading_label:    Label   = %LoadingLabel

var _current_screen: Control = null

# Single-use signal bridge: lets us await callback-based SDK calls.
# Only one async SDK operation runs at a time, so no race risk.
signal _async_done(result: Dictionary)

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	AppState.bc = get_node("/root/BrainCloud")
	AppState.bc.init(Ids.APP_SECRET, Ids.APP_ID, Ids.APP_VERSION, Ids.SERVER_URL)
	var bc_ver: String = AppState.bc.braincloud_client.get_braincloud_version()
	_version_label.text = "v%s | BC %s" % [Ids.APP_VERSION, bc_ver]
	_loading_overlay.hide()
	_show_screen(_LOGIN_SCENE)

# ── Screen management ─────────────────────────────────────────────────────────

func _show_screen(scene: PackedScene) -> void:
	if _current_screen:
		_current_screen.queue_free()
	_current_screen = scene.instantiate()
	_screen_container.add_child(_current_screen)
	_wire_screen(_current_screen)

func _wire_screen(screen: Control) -> void:
	if screen.has_signal("authenticated"):
		screen.authenticated.connect(_on_authenticated)
	if screen.has_signal("matchmake_requested"):
		screen.matchmake_requested.connect(_on_matchmake_requested)
	if screen.has_signal("leave_lobby"):
		screen.leave_lobby.connect(_on_leave_lobby)
	if screen.has_signal("end_match"):
		screen.end_match.connect(_on_host_end_match)

# ── Auth flow ─────────────────────────────────────────────────────────────────

func _on_authenticated() -> void:
	_show_screen(_MENU_SCENE)

# ── Matchmaking flow ──────────────────────────────────────────────────────────

func _on_matchmake_requested(lobby_type: String, protocol: String, use_ping: bool) -> void:
	AppState.selected_lobby_type = lobby_type
	AppState.selected_protocol   = protocol
	AppState.use_ping_data       = use_ping

	_show_loading("Enabling real-time connection...")
	var rtt_result := await _enable_rtt()
	if rtt_result.get("status", 0) != 200:
		_hide_loading()
		return

	AppState.user_cx_id = AppState.bc.rtt_service.get_rtt_connection_id()
	AppState.bc.rtt_service.register_rtt_lobby_callback(_on_rtt_lobby_event)

	_show_loading("Finding / creating lobby...")
	var lobby_result := await _find_or_create_lobby(lobby_type, use_ping)
	_hide_loading()
	if lobby_result.get("status", 0) != 200:
		_tear_down_rtt()
		_show_screen(_MENU_SCENE)   # fresh menu with button re-enabled
		return

	# Grab lobbyId from the response so update_ready calls work immediately
	AppState.lobby_id = lobby_result.get("data", {}).get("id", "")

	_show_screen(_LOBBY_SCENE)

func _enable_rtt() -> Dictionary:
	AppState.bc.rtt_service.enable_rtt(
		"WEBSOCKET",
		func(r): _async_done.emit(r),
		func(r): _async_done.emit(r)
	)
	return await _async_done

func _find_or_create_lobby(lobby_type: String, use_ping: bool) -> Dictionary:
	var algo  := {"strategy": "ranged-absolute", "alignment": "center", "ranges": [1000]}
	var extra := {"colorIndex": AppState.my_color_index, "presentSinceStart": true}
	if use_ping:
		return await AppState.bc.lobby_service.find_or_create_lobby_with_ping_data(
			lobby_type, 0, 1, algo, {}, {}, false, extra, "all", []
		)
	else:
		return await AppState.bc.lobby_service.find_or_create_lobby(
			lobby_type, 0, 1, algo, {}, {}, false, extra, "all", []
		)

# ── RTT lobby event routing ───────────────────────────────────────────────────

func _on_rtt_lobby_event(msg: Dictionary) -> void:
	var data: Dictionary = msg.get("data", {})
	var op: String       = data.get("operation", "")

	# Keep AppState authoritative so screens always read correct state
	match op:
		"MEMBER_JOIN":
			var member: Dictionary = data.get("member", {})
			if not AppState.lobby_members.any(func(m): return m.get("cxId") == member.get("cxId")):
				AppState.lobby_members.append(member)
			if AppState.lobby_id.is_empty():
				AppState.lobby_id = data.get("lobbyId", "")
		"MEMBER_LEFT":
			var cx: String = data.get("member", {}).get("cxId", "")
			AppState.lobby_members = AppState.lobby_members.filter(func(m): return m.get("cxId") != cx)
		"MEMBER_UPDATE":
			var member: Dictionary = data.get("member", {})
			var cx: String = member.get("cxId", "")
			for i in AppState.lobby_members.size():
				if AppState.lobby_members[i].get("cxId") == cx:
					AppState.lobby_members[i] = member
					break

	match op:
		"MEMBER_JOIN", "MEMBER_LEFT", "MEMBER_UPDATE", "ROOM_UPDATE":
			if _current_screen and _current_screen.has_method("on_lobby_event"):
				_current_screen.on_lobby_event(op, data)
		"STARTING":
			_show_loading("Match is starting...")
		"ROOM_ASSIGNED":
			_show_loading("Server assigned — connecting...")
		"ROOM_READY":
			await _connect_relay(data)
		"DISBANDED":
			_hide_loading()
			_tear_down_rtt()
			_show_screen(_MENU_SCENE)

# ── Relay connect ─────────────────────────────────────────────────────────────

func _connect_relay(room_data: Dictionary) -> void:
	_show_loading("Connecting to relay server...")
	var connect_data: Dictionary = room_data.get("connectData", {})
	AppState.lobby_id   = room_data.get("lobbyId", connect_data.get("lobbyId", ""))
	AppState.server_info = connect_data

	var host    : String = connect_data.get("host", "")
	var ports   : Dictionary = connect_data.get("ports", {})
	var proto   := AppState.selected_protocol.to_lower()
	var use_ssl := proto in ["wss"]
	# WS fallback: UDP/TCP modes use WS until native UDP/TCP relay is added to SDK
	var port_key := proto if ports.has(proto) else "ws"
	var port    := int(ports.get(port_key, 9301))

	var opts := {
		"host"    : host,
		"port"    : port,
		"ssl"     : use_ssl,
		"cxId"    : AppState.user_cx_id,
		"lobbyId" : AppState.lobby_id,
		"passcode": connect_data.get("passcode", "")
	}

	AppState.bc.relay_service.relay_connect(
		opts,
		func(r): _async_done.emit(r),
		func(r): _async_done.emit(r)
	)
	var relay_result: Dictionary = await _async_done
	_hide_loading()

	if relay_result.get("status", 0) == 200:
		AppState.my_net_id = AppState.bc.relay_service.get_net_id()
		AppState.bc.relay_service.register_system_callback(_on_relay_system)
		_show_screen(_GAME_SCENE)
	else:
		_show_loading("Relay connect failed.")

# ── Relay system messages ─────────────────────────────────────────────────────

func _on_relay_system(msg: Dictionary) -> void:
	var op: String = msg.get("op", "")
	match op:
		"END_MATCH":
			_on_match_ended()
		_:
			if _current_screen and _current_screen.has_method("on_relay_system"):
				_current_screen.on_relay_system(op, msg)

func _on_host_end_match() -> void:
	# Local player is host and pressed end — send END_MATCH then clean up
	var bytes := JSON.stringify({"op": "end_match"}).to_utf8_buffer()
	AppState.bc.relay_service.send(bytes, BrainCloudRelay.TO_ALL_PLAYERS, true, true, 0)
	_on_match_ended()

func _on_match_ended() -> void:
	AppState.bc.relay_service.deregister_relay_callback()
	AppState.bc.relay_service.deregister_system_callback()
	AppState.bc.relay_service.relay_disconnect()
	AppState.lobby_members.clear()
	_show_screen(_LOBBY_SCENE)

# ── Leave lobby ───────────────────────────────────────────────────────────────

func _on_leave_lobby() -> void:
	AppState.bc.lobby_service.leave_lobby(AppState.lobby_id)
	_tear_down_rtt()
	_show_screen(_MENU_SCENE)

func _tear_down_rtt() -> void:
	AppState.bc.rtt_service.deregister_rtt_lobby_callback()
	AppState.bc.rtt_service.disable_rtt()
	AppState.lobby_id = ""
	AppState.lobby_members.clear()

# ── Loading overlay ───────────────────────────────────────────────────────────

func _show_loading(text: String) -> void:
	_loading_label.text = text
	_loading_overlay.show()
	_loading_overlay.move_to_front()

func _hide_loading() -> void:
	_loading_overlay.hide()
