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
@onready var _cancel_btn:       Button  = %CancelButton

var _current_screen: Control = null
var _lobby_search_start: int  = 0   # non-zero while find_or_create_lobby is in-flight
var _search_cancelled:  bool  = false

# Single-use signal bridge: lets us await callback-based SDK calls.
# Only one async SDK operation runs at a time, so no race risk.
signal _async_done(result: Dictionary)

# ── Lifecycle ─────────────────────────────────────────────────────────────────

func _ready() -> void:
	AppState.bc = get_node("/root/BrainCloud")
	AppState.bc.init(Ids.APP_SECRET, Ids.APP_ID, Ids.APP_VERSION, Ids.SERVER_URL)
	AppState.bc.braincloud_client.enable_logging(true)
	var bc_ver: String = AppState.bc.braincloud_client.get_braincloud_version()
	_version_label.text = "v%s\nBC %s" % [Ids.APP_VERSION, bc_ver]
	_loading_overlay.hide()
	_cancel_btn.pressed.connect(_on_cancel_search_pressed)
	_show_screen(_LOGIN_SCENE)

# ── Tick ──────────────────────────────────────────────────────────────────────

func _process(_delta: float) -> void:
	if _lobby_search_start <= 0:
		return
	var elapsed := int((Time.get_ticks_msec() - _lobby_search_start) / 1000)
	_loading_label.text = "Searching for lobby...  %d:%02d" % [elapsed / 60, elapsed % 60]

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
	if screen.has_signal("leave_game"):
		screen.leave_game.connect(_on_leave_game_requested)

# ── Auth flow ─────────────────────────────────────────────────────────────────

func _on_authenticated() -> void:
	_show_screen(_MENU_SCENE)

# ── Matchmaking flow ──────────────────────────────────────────────────────────

func _on_matchmake_requested(lobby_type: String, protocol: String, use_ping: bool) -> void:
	AppState.selected_lobby_type = lobby_type
	AppState.selected_protocol   = protocol
	AppState.use_ping_data       = use_ping
	_search_cancelled            = false

	_show_loading("Enabling real-time connection...")
	var rtt_result := await _enable_rtt()
	if rtt_result.get("status", 0) != 200:
		_hide_loading()
		return

	AppState.user_cx_id = AppState.bc.rtt_service.get_rtt_connection_id()
	AppState.bc.rtt_service.register_rtt_lobby_callback(_on_rtt_lobby_event)

	# Collect region ping data before matchmaking when requested
	if use_ping:
		await _fetch_and_ping_regions(lobby_type)

	_lobby_search_start = Time.get_ticks_msec()
	_show_loading("Searching for lobby...  0:00")
	_cancel_btn.show()
	var lobby_result := await _find_or_create_lobby(lobby_type, use_ping)
	_cancel_btn.hide()
	_lobby_search_start = 0

	# Cancel button may have fired while we were awaiting — bail out if so.
	if _search_cancelled:
		return

	_hide_loading()
	if lobby_result.get("status", 0) != 200:
		_tear_down_rtt()
		_show_screen(_MENU_SCENE)
		return

	AppState.lobby_id = lobby_result.get("data", {}).get("id", "")

	_show_screen(_LOBBY_SCENE)

func _on_cancel_search_pressed() -> void:
	_search_cancelled = true
	_cancel_btn.hide()
	_lobby_search_start = 0
	_tear_down_rtt()
	_hide_loading()
	_show_screen(_MENU_SCENE)

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
	if use_ping and not AppState.ping_data.is_empty():
		extra["pings"] = AppState.ping_data
		AppState.bc.lobby_service.set_ping_data(AppState.ping_data)
		return await AppState.bc.lobby_service.find_or_create_lobby_with_ping_data(
			lobby_type, 0, 1, algo, {}, {}, false, extra, "all", []
		)
	else:
		return await AppState.bc.lobby_service.find_or_create_lobby(
			lobby_type, 0, 1, algo, {}, {}, false, extra, "all", []
		)

# ── Region ping (for use_ping_data path) ─────────────────────────────────────

func _fetch_and_ping_regions(lobby_type: String) -> void:
	_show_loading("Getting regions...")
	var result: Dictionary = await AppState.bc.lobby_service.get_regions_for_lobbies([lobby_type])
	if result.get("status", 0) != 200:
		return

	var region_ping_data: Dictionary = result.get("data", {}).get("regionPingData", {})
	if region_ping_data.is_empty():
		return

	AppState.ping_data.clear()
	var regions: Array = region_ping_data.keys()
	_show_loading("Pinging %d region(s)..." % regions.size())

	for region: String in regions:
		var info: Dictionary = region_ping_data[region]
		var target: String   = info.get("target", "")
		var ping_port: int   = int(info.get("pingPort", 80))
		if target.is_empty():
			AppState.ping_data[region] = 999
			continue

		var url := "http://%s:%d/" % [target, ping_port]
		var http := HTTPRequest.new()
		http.timeout = 2.0
		add_child(http)
		var start_ms := Time.get_ticks_msec()
		if http.request(url, [], HTTPClient.METHOD_HEAD) == OK:
			await http.request_completed
			AppState.ping_data[region] = mini(int(Time.get_ticks_msec() - start_ms), 999)
		else:
			AppState.ping_data[region] = 999
		http.queue_free()

# ── RTT lobby event routing ───────────────────────────────────────────────────

func _on_rtt_lobby_event(msg: Dictionary) -> void:
	var data: Dictionary = msg.get("data", {})
	var op: String       = msg.get("operation", "")  # operation is top-level in RTT msg, not inside data

	# Parse lobby owner from any event that carries a lobby object
	var lobby_obj: Dictionary = data.get("lobby", {})
	if not lobby_obj.is_empty():
		var owner: String = lobby_obj.get("ownerCxId", "")
		if not owner.is_empty():
			AppState.lobby_owner_cx_id = owner

	# Keep AppState authoritative so screens always read correct state
	match op:
		"MEMBER_JOIN":
			# Rebuild from the full lobby.members list so late-joiners and screens that
			# loaded before this event both end up with the correct complete member list.
			var all_members: Array = lobby_obj.get("members", [])
			if not all_members.is_empty():
				AppState.lobby_members.clear()
				for m in all_members:
					if m is Dictionary:
						AppState.lobby_members.append(m)
			else:
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
		"STARTING", "ROOM_ASSIGNED", "ROOM_READY":
			if _current_screen and _current_screen.has_method("on_lobby_event"):
				_current_screen.on_lobby_event(op, data)
			if op == "ROOM_READY":
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

	var host  : String     = connect_data.get("address", "")
	var ports : Dictionary = connect_data.get("ports", {})

	# i3D and GameLift servers expose a single port under their own key; force WS for those.
	# Port values come in as floats from JSON — cast explicitly to int.
	var proto   := AppState.selected_protocol.to_lower()
	var use_ssl := proto in ["wss"]
	var port    : int
	if ports.has("gamelift"):
		port  = int(ports["gamelift"])
		proto = "ws"
	elif ports.has("i3d"):
		port  = int(ports["i3d"])
		proto = "ws"
	elif ports.has(proto):
		port = int(ports[proto])
	elif ports.has("ws"):
		port = int(ports["ws"])
	else:
		port = int(ports.values()[0]) if not ports.is_empty() else 9301

	# WSS stays SSL; WS stays plain. Relay servers do not use TLS.

	print("[Relay] room_data keys: ", room_data.keys())
	print("[Relay] connectData: ", connect_data)
	print("[Relay] host='%s' port=%d proto='%s' ssl=%s" % [host, port, proto, str(use_ssl)])
	print("[Relay] cxId='%s' lobbyId='%s'" % [AppState.user_cx_id, AppState.lobby_id])

	var opts := {
		"host"    : host,
		"port"    : port,
		"ssl"     : use_ssl,
		"cxId"    : AppState.user_cx_id,
		"lobbyId" : AppState.lobby_id,
		"passcode": room_data.get("passcode", "")
	}

	AppState.bc.relay_service.relay_connect(
		opts,
		func(r): _async_done.emit(r),
		func(r): _async_done.emit(r)
	)
	var relay_result: Dictionary = await _async_done
	_hide_loading()
	print("[Relay] connect result: ", relay_result)

	if relay_result.get("status", 0) == 200:
		AppState.my_net_id = AppState.bc.relay_service.get_net_id()
		AppState.bc.relay_service.register_system_callback(_on_relay_system)

		# Host records start time and broadcasts game_start to all players so timers sync
		if AppState.user_cx_id == AppState.lobby_owner_cx_id:
			AppState.game_start_time_ms = int(Time.get_unix_time_from_system() * 1000.0)
			var start_msg := JSON.stringify({
				"op": "game_start",
				"data": {"startTime": AppState.game_start_time_ms}
			})
			AppState.bc.relay_service.send(
				start_msg.to_utf8_buffer(),
				BrainCloudRelay.TO_ALL_PLAYERS,
				true, false, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1
			)

		_show_screen(_GAME_SCENE)
	else:
		var msg_text: String = relay_result.get("status_message", "Relay connect failed.")
		_show_loading("Relay connect failed.\n%s" % msg_text)
		await get_tree().create_timer(3.0).timeout
		_hide_loading()
		_show_screen(_LOBBY_SCENE)

# ── Relay system messages ─────────────────────────────────────────────────────

func _on_relay_system(msg: Dictionary) -> void:
	var op: String = msg.get("op", "")
	match op:
		"END_MATCH":
			_on_match_ended()
		"DISCONNECT":
			# Relay server dropped the connection unexpectedly — treat as match end
			_on_match_ended()
		_:
			if _current_screen and _current_screen.has_method("on_relay_system"):
				_current_screen.on_relay_system(op, msg)

func _on_host_end_match() -> void:
	# Only the host sends the SDK endMatch packet (opcode CL2RS_ENDMATCH=6).
	# The relay server broadcasts END_MATCH as a system message to all players,
	# which triggers _on_relay_system → _on_match_ended() for everyone including the host.
	if AppState.user_cx_id == AppState.lobby_owner_cx_id:
		AppState.bc.relay_service.end_match({
			"cxId":    AppState.user_cx_id,
			"lobbyId": AppState.lobby_id,
			"op":      "END_MATCH"
		})

func _on_leave_game_requested() -> void:
	_on_match_ended()

func _on_match_ended() -> void:
	AppState.bc.relay_service.deregister_relay_callback()
	AppState.bc.relay_service.deregister_system_callback()
	AppState.bc.relay_service.relay_disconnect()
	AppState.game_start_time_ms = 0
	# Non-host players re-ready for the next round (mirrors C++ updateReady(true))
	if AppState.user_cx_id != AppState.lobby_owner_cx_id and AppState.lobby_id != "":
		AppState.bc.lobby_service.update_ready(
			AppState.lobby_id, true,
			{"colorIndex": AppState.my_color_index, "pings": AppState.ping_data}
		)
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
	AppState.lobby_owner_cx_id = ""
	AppState.lobby_members.clear()
	AppState.ping_data.clear()

# ── Loading overlay ───────────────────────────────────────────────────────────

func _show_loading(text: String) -> void:
	_loading_label.text = text
	_loading_overlay.show()
	_loading_overlay.move_to_front()

func _hide_loading() -> void:
	_loading_overlay.hide()
