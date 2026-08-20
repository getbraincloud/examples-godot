# Copyright 2026 bitHeads, Inc. All Rights Reserved.
extends Control

const _LOGIN_SCENE          := preload("res://Scenes/Screens/LoginScreen.tscn")
const _MENU_SCENE           := preload("res://Scenes/Screens/MainMenuScreen.tscn")
const _LOBBY_SCENE          := preload("res://Scenes/Screens/LobbyScreen.tscn")
const _GAME_SCENE           := preload("res://Scenes/Screens/GameScreen.tscn")
const _MATCH_SUMMARY_SCENE  := preload("res://Scenes/Screens/MatchSummaryScreen.tscn")

@onready var _screen_container: Control = %ScreenContainer
@onready var _version_label:    Label   = %VersionLabel
@onready var _loading_overlay:  Control = %LoadingOverlay
@onready var _loading_label:    Label   = %LoadingLabel
@onready var _cancel_btn:       Button  = %CancelButton

var _current_screen: Control = null
var _lobby_search_start: int  = 0   # non-zero across the whole ping-regions + find-lobby flow
var _in_lobby_search_step: bool = false  # true only once find_or_create_lobby itself is in-flight
var _search_cancelled:  bool  = false

# Single-use signal bridge: lets us await callback-based SDK calls.
# Only one async SDK operation runs at a time, so no race risk.
signal _async_done(result: Dictionary)

# RTT enable is idempotent/concurrency-safe: the main menu fires it eagerly (so chat is ready
# before it's needed) and matchmaking also awaits it — whichever gets there first actually
# calls enable_rtt; later callers just wait on _rtt_ready.
var _rtt_enabling: bool = false
signal _rtt_ready(result: Dictionary)

# Non-host: throttle state for polling the GlobalEntity PostMatchResults.js writes (indexed
# by "<lobbyId>:<round>") instead of waiting on a host relay broadcast — see
# _tick_match_results_poll(). Reset whenever a new round's match result shows up.
var _results_poll_round: int = -1
var _results_poll_accum: float = 0.0
var _results_poll_in_flight: bool = false
const _RESULTS_POLL_INTERVAL_SEC := 1.0

# ── Lifecycle ─────────────────────────────────────────────────────────────────

const _CREDS_PATH := "res://addons/braincloud/braincloud.cfg"

func _ready() -> void:
	AppState.bc = get_node("/root/brainCloud")
	var app_secret := _bc_setting("app_secret", Ids.APP_SECRET)
	var app_id     := _bc_setting("app_id",     Ids.APP_ID)
	var app_ver    := _bc_setting("app_version", Ids.APP_VERSION)
	var server_url := _bc_setting("server_url", Ids.SERVER_URL)
	AppState.bc.init(app_secret, app_id, app_ver, server_url)
	AppState.bc.braincloud_client.enable_compression(true)
	var enable_log := bool(ProjectSettings.get_setting("braincloud/debug/enable_logging", false))
	AppState.bc.braincloud_client.enable_logging(enable_log)
	var bc_ver: String = AppState.bc.braincloud_client.get_braincloud_version()
	_version_label.text = "v%s\nBC %s" % [app_ver, bc_ver]
	_loading_overlay.hide()
	_cancel_btn.pressed.connect(_on_cancel_search_pressed)
	_show_screen(_LOGIN_SCENE)


# Reads a brainCloud credential or config value. Credentials (app_id, app_secret)
# come from the gitignored braincloud.cfg; other keys come from ProjectSettings.
# Falls back to Ids.gd constants in both cases.
func _bc_setting(key: String, fallback: String) -> String:
	if key in ["app_id", "app_secret"]:
		var cfg := ConfigFile.new()
		if cfg.load(_CREDS_PATH) == OK:
			var v := str(cfg.get_value("credentials", key, ""))
			if not v.is_empty():
				return v
		return fallback
	var setting := "braincloud/config/" + key
	if ProjectSettings.has_setting(setting):
		var val: String = str(ProjectSettings.get_setting(setting, ""))
		if not val.is_empty():
			return val
	return fallback

# ── Tick ──────────────────────────────────────────────────────────────────────

func _process(delta: float) -> void:
	_tick_match_results_poll(delta)

	if _lobby_search_start <= 0 or not _in_lobby_search_step:
		return
	var elapsed := int((Time.get_ticks_msec() - _lobby_search_start) / 1000)
	var text := "Searching for lobby...  %d:%02d" % [elapsed / 60, elapsed % 60]
	# Past a minute, a lobby type with no warm server yet may still be spinning one up —
	# matches JS's LoadingScreen "server may be warming up" warning at the same threshold.
	if elapsed >= 60:
		text += "\nServer may be warming up..."
	_loading_label.text = text

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
	if screen.has_signal("lobby_signal_send_requested"):
		screen.lobby_signal_send_requested.connect(_on_lobby_signal_send_requested)
	if screen.has_signal("global_chat_send_requested"):
		screen.global_chat_send_requested.connect(_on_global_chat_send_requested)
	if screen.has_signal("host_match_result_ready"):
		screen.host_match_result_ready.connect(_on_host_match_result_ready)
	if screen.has_signal("rematch_ready_changed"):
		screen.rematch_ready_changed.connect(_on_rematch_ready_changed)
	if screen.has_signal("main_menu_requested"):
		screen.main_menu_requested.connect(_on_match_summary_main_menu_requested)

# ── Auth flow ─────────────────────────────────────────────────────────────────

func _on_authenticated() -> void:
	_show_screen(_MENU_SCENE)
	# Fire-and-forget: gets RTT up before the player ever opens chat, on either the main menu
	# or the lobby screen (both embed GlobalChatPanel).
	_connect_global_chat()

# Enables RTT, then resolves + connects the shared "gl" global chat channel and registers the
# live RTT push callback — both survive GlobalChatPanel being recreated on every screen
# transition (state lives in AppState, not on the panel). Fire-and-forget from _on_authenticated.
func _connect_global_chat() -> void:
	var rtt_result: Dictionary = await _ensure_rtt_enabled()
	if rtt_result.get("status", 0) != 200:
		return
	if not AppState.global_chat_channel_id.is_empty():
		return # already connected (shouldn't normally be called twice, but stay idempotent)

	var result: Dictionary = await AppState.bc.chat_service.get_channel_id("gl", "gl")
	if result.get("status", 0) != 200:
		return
	var channel_id: String = result.get("data", {}).get("channelId", "")
	if channel_id.is_empty():
		return

	var connect_result: Dictionary = await AppState.bc.chat_service.channel_connect(channel_id, 30)
	if connect_result.get("status", 0) != 200:
		return
	AppState.global_chat_channel_id = channel_id

	var messages: Array = connect_result.get("data", {}).get("messages", [])
	# Server already returns these ascending/oldest-first (it re-sorts from its native
	# descending storage order before responding), so no reverse needed. This is just the
	# one-time initial history — everything after arrives via _on_rtt_chat_event already in
	# order and gets appended directly.
	AppState.global_chat_history.clear()
	for m: Dictionary in messages:
		AppState.global_chat_history.append({
			"msg_id": m.get("msgId", ""),
			"from_name": m.get("from", {}).get("name", "Player"),
			"text": m.get("content", {}).get("text", ""),
		})
	if _current_screen and _current_screen.has_method("add_global_chat_message"):
		for m: Dictionary in AppState.global_chat_history:
			_current_screen.add_global_chat_message(m["msg_id"], m["from_name"], m["text"])

	AppState.bc.rtt_service.register_rtt_chat_callback(_on_rtt_chat_event)

# Live push for the global chat channel (goes to every connected member, including the sender
# — see knowledge-articles/01-chat.md). New messages, edits, and deletes all arrive on this
# same event, keyed by msg_id; if a message already scrolled out of history it just won't be
# found below, which is fine — nothing to update.
func _on_rtt_chat_event(msg: Dictionary) -> void:
	var data: Dictionary = msg.get("data", {})
	var operation: String = msg.get("operation", "")
	var msg_id: String = data.get("msgId", "")

	if operation == "INCOMING":
		var from_name: String = data.get("from", {}).get("name", "Player")
		var text: String = data.get("content", {}).get("text", "")
		AppState.global_chat_history.append({"msg_id": msg_id, "from_name": from_name, "text": text})
		if _current_screen and _current_screen.has_method("add_global_chat_message"):
			_current_screen.add_global_chat_message(msg_id, from_name, text)
	elif operation == "UPDATE":
		# Same shape as INCOMING (full message, edited content) — update in place so it
		# doesn't jump to the bottom of the scroll.
		var text: String = data.get("content", {}).get("text", "")
		for entry: Dictionary in AppState.global_chat_history:
			if entry.get("msg_id", "") == msg_id:
				entry["text"] = text
				break
		if _current_screen and _current_screen.has_method("update_global_chat_message"):
			_current_screen.update_global_chat_message(msg_id, text)
	elif operation == "DELETE":
		# DELETE's payload is just {chId, msgId} — no content/from — so only msg_id is usable here.
		for i in range(AppState.global_chat_history.size() - 1, -1, -1):
			if AppState.global_chat_history[i].get("msg_id", "") == msg_id:
				AppState.global_chat_history.remove_at(i)
				break
		if _current_screen and _current_screen.has_method("remove_global_chat_message"):
			_current_screen.remove_global_chat_message(msg_id)

func _on_global_chat_send_requested(text: String) -> void:
	if text.is_empty() or AppState.global_chat_channel_id.is_empty():
		return
	AppState.bc.chat_service.post_chat_message_simple(AppState.global_chat_channel_id, text, true)

# ── Matchmaking flow ──────────────────────────────────────────────────────────

func _on_matchmake_requested(lobby_type: String, protocol: String, use_ping: bool) -> void:
	AppState.selected_lobby_type = lobby_type
	AppState.selected_protocol   = protocol
	AppState.use_ping_data       = use_ping
	_search_cancelled            = false

	_show_loading("Enabling real-time connection...")
	var rtt_result := await _ensure_rtt_enabled()
	if rtt_result.get("status", 0) != 200:
		push_error("enable_rtt failed: ", rtt_result)
		_hide_loading()
		return

	# Cancel is available for the whole rest of the flow (region pinging through lobby
	# search), not just the final "Searching for lobby" step — mirrors cpp, whose loading
	# dialog and elapsed timer run continuously across both phases as one JoiningLobby state.
	_lobby_search_start = Time.get_ticks_msec()
	_cancel_btn.show()

	# Collect region ping data before matchmaking when requested
	if use_ping:
		await _fetch_and_ping_regions(lobby_type)
		if _search_cancelled:
			return

	_in_lobby_search_step = true
	_show_loading("Searching for lobby...  0:00")
	var lobby_result := await _find_or_create_lobby(lobby_type, use_ping)
	_cancel_btn.hide()
	_lobby_search_start = 0
	_in_lobby_search_step = false

	# Cancel button may have fired while we were awaiting — bail out if so.
	if _search_cancelled:
		return

	_hide_loading()
	if lobby_result.get("status", 0) != 200:
		# Previously silent (straight back to the menu with no explanation, unlike cpp's
		# errorAndReturnToMenu popup). push_error surfaces in the browser dev console (F12)
		# on Web export, matching the react RelayTestApp's console.error on the same failure.
		push_error("find_or_create_lobby failed: ", lobby_result)
		_tear_down_rtt()
		_show_screen(_MENU_SCENE)
		return

	AppState.lobby_id = lobby_result.get("data", {}).get("id", "")

	_show_screen(_LOBBY_SCENE)

func _on_cancel_search_pressed() -> void:
	_search_cancelled = true
	_cancel_btn.hide()
	_lobby_search_start = 0
	_in_lobby_search_step = false
	_tear_down_rtt()
	_hide_loading()
	_show_screen(_MENU_SCENE)

# enable_rtt silently no-ops (no callback at all) if called again while already
# enabled/connecting, so a second concurrent caller can't just call it again — it queues on
# _rtt_ready instead. Only ever one enable_rtt in flight at a time.
func _ensure_rtt_enabled() -> Dictionary:
	if AppState.bc.rtt_service.is_rtt_enabled():
		return {"status": 200}
	if _rtt_enabling:
		return await _rtt_ready

	_rtt_enabling = true
	AppState.bc.rtt_service.enable_rtt(
		"WEBSOCKET",
		func(r): _async_done.emit(r),
		func(r): _async_done.emit(r)
	)
	var result: Dictionary = await _async_done
	_rtt_enabling = false
	if result.get("status", 0) == 200:
		AppState.user_cx_id = AppState.bc.rtt_service.get_rtt_connection_id()
		AppState.bc.rtt_service.register_rtt_lobby_callback(_on_rtt_lobby_event)
	_rtt_ready.emit(result)
	return result

func _find_or_create_lobby(lobby_type: String, use_ping: bool) -> Dictionary:
	var algo  := {"strategy": "ranged-absolute", "alignment": "center", "ranges": [1000]}
	var extra := {"colorIndex": AppState.my_color_index, "presentSinceStart": true}
	# CPP mainMenu.cpp: a "Team*" lobby type joins team "alpha" (CPP's UI also lets you pick
	# Beta); every other lobby type uses "all". Sending "all" for a Team* lobby breaks team
	# grouping and interop with the CPP/C# clients.
	var team_code := "alpha" if lobby_type.begins_with("Team") else "all"
	if use_ping and not AppState.ping_data.is_empty():
		extra["pings"] = AppState.ping_data
		AppState.bc.lobby_service.set_ping_data(AppState.ping_data)
		return await AppState.bc.lobby_service.find_or_create_lobby_with_ping_data(
			lobby_type, 0, 1, algo, {}, {}, false, extra, team_code, []
		)
	else:
		return await AppState.bc.lobby_service.find_or_create_lobby(
			lobby_type, 0, 1, algo, {}, {}, false, extra, team_code, []
		)

# ── Region ping (for use_ping_data path) ─────────────────────────────────────

func _fetch_and_ping_regions(lobby_type: String) -> void:
	_show_loading("Getting regions...")
	# Clear up front (not just on success) — a stale ping_data left over from a previous
	# lobby type/attempt would otherwise get sent to find_or_create_lobby_with_ping_data
	# for regions that don't apply to this lobby type if either call below fails/no-ops.
	AppState.ping_data.clear()
	var result: Dictionary = await AppState.bc.lobby_service.get_regions_for_lobbies([lobby_type])
	if result.get("status", 0) != 200:
		push_error("get_regions_for_lobbies failed: ", result)
		return

	var regions: Array = AppState.bc.lobby_service.get_ping_target_regions()
	if regions.is_empty():
		return

	# Live per-region status list — one line per region, updated as each ping resolves
	# (mirrors cpp's loading dialog: "region: pinging..." -> "region: N ms" / "region: T/O" —
	# not just a single static "Pinging N region(s)..." message for the whole batch). The actual
	# pinging (timing, averaging, drop-slowest) now lives in the SDK's ping_regions(), matching
	# cpp/csharp/js's client-side PingRegions — this just drives the loading-screen text off its
	# per-region progress callback.
	var region_status: Dictionary = {}
	for region: String in regions:
		region_status[region] = "pinging..."
	_show_region_ping_status(regions, region_status)

	AppState.ping_data = await AppState.bc.lobby_service.ping_regions(
		func(region: String, ms: int) -> void:
			region_status[region] = "T/O" if ms >= 999 else "%d ms" % ms
			_show_region_ping_status(regions, region_status)
	)

func _show_region_ping_status(regions: Array, region_status: Dictionary) -> void:
	var lines: Array = ["Pinging %d region(s)..." % regions.size(), ""]
	for region: String in regions:
		lines.append("%s: %s" % [region, region_status[region]])
	_show_loading("\n".join(lines))

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
		"STARTING", "ROOM_ASSIGNED", "ROOM_PROGRESS", "ROOM_READY":
			if _current_screen and _current_screen.has_method("on_lobby_event"):
				_current_screen.on_lobby_event(op, data)
			if op == "ROOM_READY":
				await _connect_relay(data)
		"SIGNAL":
			_on_lobby_signal_received(data)
		"DISBANDED":
			_hide_loading()
			_tear_down_rtt()
			_show_screen(_MENU_SCENE)

# This-lobby chat, via the Lobby service's send_signal. "from" is the server's authoritative
# sender info. Skip echoes of our own signal (compared by cxId, not name, since two players
# could share a display name) — we already appended it locally in
# _on_lobby_signal_send_requested.
func _on_lobby_signal_received(data: Dictionary) -> void:
	if not data.has("from") or not data.has("signalData"):
		return
	var from: Dictionary = data.get("from", {})
	var signal_data: Dictionary = data.get("signalData", {})
	var from_cx_id: String = from.get("cxId", "")
	var from_name: String = from.get("name", "Player")
	var text: String = signal_data.get("text", "")
	if text.is_empty() or from_cx_id == AppState.user_cx_id:
		return

	AppState.lobby_chat_history.append({"from": from_name, "text": text, "is_me": false})
	if _current_screen and _current_screen.has_method("add_lobby_chat_message"):
		_current_screen.add_lobby_chat_message(from_name, text, false)

# Sends a this-lobby chat message via the Lobby service's send_signal. Appends locally right
# away — _on_lobby_signal_received skips the echo of our own signal, which the server does
# send back to us too.
func _on_lobby_signal_send_requested(text: String) -> void:
	if text.is_empty() or AppState.lobby_id.is_empty():
		return

	AppState.bc.lobby_service.send_signal(AppState.lobby_id, {"text": text})

	AppState.lobby_chat_history.append({"from": AppState.username, "text": text, "is_me": true})
	if _current_screen and _current_screen.has_method("add_lobby_chat_message"):
		_current_screen.add_lobby_chat_message(AppState.username, text, true)

# ── Relay connect ─────────────────────────────────────────────────────────────

func _connect_relay(room_data: Dictionary) -> void:
	# Non-blocking: shown inline on the (still fully interactive, chat included) lobby screen
	# rather than a full-screen loading overlay — mirrors STARTING/ROOM_ASSIGNED/ROOM_READY,
	# which on_lobby_event already surfaces the same way.
	if _current_screen and _current_screen.has_method("set_status_override"):
		_current_screen.set_status_override("Connecting to relay server...")
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
		use_ssl = false
	elif ports.has("i3d"):
		port  = int(ports["i3d"])
		proto = "ws"
		use_ssl = false
	elif ports.has(proto):
		port = int(ports[proto])
	elif ports.has("ws"):
		# No dedicated port under the requested proto (e.g. "wss" was picked but the server
		# only handed back "ws") — fall back to the plain port AND drop SSL with it. Relay
		# servers don't terminate TLS on the bare "ws" port, so keeping use_ssl/proto at
		# "wss" here attempts a TLS handshake against a plain socket and always fails
		# (mbedtls handshake error, connection closed before completion).
		port    = int(ports["ws"])
		proto   = "ws"
		use_ssl = false
	else:
		port = int(ports.values()[0]) if not ports.is_empty() else 9301
		use_ssl = false

	# Godot's Web export just runs the browser's native WebSocket — no raw TCP/UDP sockets
	# exist in a browser (no StreamPeerTCP/PacketPeerUDP), and it can't open a plain ws://
	# connection from this page either since it's served over https (mixed-content policy,
	# no client-side way around it). So on web we ignore whatever protocol was picked on the
	# lobby screen and force the secure websocket port whenever the server offers one.
	if OS.has_feature("web") and ports.has("wss"):
		proto   = "wss"
		use_ssl = true
		port    = int(ports["wss"])

	# WSS must dial the secure hostname, not the raw IP in "address" — the TLS endpoint is
	# SNI-routed (matches JS's connectRelay: host = secureAddress || address), so a raw-IP
	# connection can't be matched to a cert/backend and just hangs instead of erroring.
	if proto == "wss":
		host = connect_data.get("secureAddress", host)

	print("[Relay] room_data keys: ", room_data.keys())
	print("[Relay] connectData: ", connect_data)
	print("[Relay] host='%s' port=%d proto='%s' ssl=%s" % [host, port, proto, str(use_ssl)])
	print("[Relay] cxId='%s' lobbyId='%s'" % [AppState.user_cx_id, AppState.lobby_id])

	var opts := {
		"host"    : host,
		"port"    : port,
		"ssl"     : use_ssl,
		"protocol": proto,
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
	print("[Relay] connect result: ", relay_result)

	if relay_result.get("status", 0) == 200:
		AppState.my_net_id = AppState.bc.relay_service.get_net_id()
		AppState.bc.relay_service.register_system_callback(_on_relay_system)
		AppState.bc.relay_service.register_relay_callback(_on_relay_data)

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
		if _current_screen and _current_screen.has_method("set_status_override"):
			_current_screen.set_status_override("Relay connect failed: %s — retrying shortly." % msg_text)
		await get_tree().create_timer(3.0).timeout
		_show_screen(_LOBBY_SCENE)

# ── Relay system messages ─────────────────────────────────────────────────────

func _on_relay_system(msg: Dictionary) -> void:
	var op: String = msg.get("op", "")
	match op:
		"END_MATCH":
			_on_match_ended_with_results()
		"DISCONNECT" when not msg.has("cxId"):
			# A "DISCONNECT" with no cxId is BrainCloudRelayComms' own transport-level signal
			# that OUR socket was closed by the server — not the RSMG peer-left notification
			# (which always carries the departing peer's cxId and must NOT end the match; see
			# the `_:` branch below, which routes it to GameScreen's per-member handling, same
			# as cpp/js/C#). Treat as match end. _matchResult may not be populated yet (a
			# manual/abrupt end skips TickMatch's broadcast sequence); the summary screen just
			# shows "Waiting for results..." in that case.
			_on_match_ended_with_results()
		_:
			if _current_screen and _current_screen.has_method("on_relay_system"):
				_current_screen.on_relay_system(op, msg)

# Registered as the single relay data callback (see _connectToRelay above) so every gameplay
# op forwards to whichever screen is current.
func _on_relay_data(net_id: int, raw: PackedByteArray) -> void:
	var msg = JSON.parse_string(raw.get_string_from_utf8())
	if not msg is Dictionary:
		return
	if _current_screen and _current_screen.has_method("on_relay_message"):
		_current_screen.on_relay_message(net_id, raw)

func _on_host_end_match() -> void:
	# Only the host sends the SDK endMatch packet (opcode CL2RS_ENDMATCH=6).
	# The relay server broadcasts END_MATCH as a system message to all players,
	# which triggers _on_relay_system → _on_match_ended_with_results() for everyone.
	if AppState.user_cx_id == AppState.lobby_owner_cx_id:
		AppState.bc.relay_service.end_match({
			"cxId":    AppState.user_cx_id,
			"lobbyId": AppState.lobby_id,
			"op":      "END_MATCH"
		})

# Voluntary early leave — skips the results screen entirely and leaves the lobby, matching
# "Exit Match" on every other platform (dotnet/react/Godot C#).
func _on_leave_game_requested() -> void:
	_teardown_relay()
	AppState.game_start_time_ms = 0
	if not AppState.lobby_id.is_empty():
		AppState.bc.lobby_service.leave_lobby(AppState.lobby_id)
	_tear_down_rtt()
	_show_screen(_MENU_SCENE)

# The Match Summary + rematch-queue screen (BCLOUD-14489). match_result is usually already
# populated by the time END_MATCH arrives (TickMatch's auto-end sequence broadcasts it
# ResultGraceSec before ending the match; a manual early end skips that, so the screen just
# shows "Waiting for results..." there).
func _on_match_ended_with_results() -> void:
	# Give GameScreen a chance to fill in AppState.match_result_entries locally before it's
	# freed below — otherwise a late join or a dropped match_result broadcast leaves the
	# summary screen permanently empty for this round (nothing left alive can recompute it).
	if _current_screen and _current_screen.has_method("apply_fallback_if_missing"):
		_current_screen.apply_fallback_if_missing()

	_teardown_relay()
	AppState.game_start_time_ms = 0

	# Clear readiness both locally and server-side right away, or the Match Summary "Queue for
	# Rematch N/M" count would start from our stale pre-match ready state. AppState.user_is_ready
	# is what MatchSummaryScreen shows for our own row; the update_ready call just keeps the
	# server (and everyone else's view of us) in sync, and it's fine if that round trip lags a
	# few seconds since nothing local waits on it.
	AppState.user_is_ready = false
	_show_screen(_MATCH_SUMMARY_SCENE)

	if not AppState.lobby_id.is_empty():
		var result: Dictionary = await AppState.bc.lobby_service.update_ready(
			AppState.lobby_id, false,
			{"colorIndex": AppState.my_color_index, "pings": AppState.ping_data}
		)
		_recover_if_lobby_gone(result)

const _LOBBY_NOT_FOUND_REASON := 40613

# The lobby can get torn down server-side between our own actions — e.g. a rematch round
# drops to one remaining player and the server disbands it before our next lobby call fires,
# which then 400s with "Unrecognized lobby". Treat that like a DISBANDED event: tear down and
# head back to the main menu instead of getting stuck retrying lobby actions (Queue for
# Rematch, ready-up, etc.) against a dead lobbyId.
# Returns true if the caller should stop (the lobby is gone and we've already bailed out).
func _recover_if_lobby_gone(result: Dictionary) -> bool:
	if result.get("status", 0) == 200:
		return false
	if int(result.get("reason_code", 0)) != _LOBBY_NOT_FOUND_REASON:
		return false
	_hide_loading()
	_tear_down_rtt()
	_show_screen(_MENU_SCENE)
	return true

func _teardown_relay() -> void:
	AppState.bc.relay_service.deregister_relay_callback()
	AppState.bc.relay_service.deregister_system_callback()
	AppState.bc.relay_service.relay_disconnect()

# ── Match result / leaderboard posting (BCLOUD-14472/14489/14490) ─────────────

func _on_host_match_result_ready(round: int, entries: Array) -> void:
	_host_post_match_results_to_cloud(round, entries)

# Host-only: posts the whole round's results to the four leaderboards in one trusted
# server-side call (the PostMatchResults cloud script — see brainCloud Design > Cloud Code).
# Lives in Main rather than GameScreen because the cloud-script response can land after the
# round has ended and we've already moved on to the Match Summary screen — Main survives that
# transition, GameScreen doesn't.
func _host_post_match_results_to_cloud(round: int, entries: Array) -> void:
	var members_by_cx: Dictionary = {}
	for m: Dictionary in AppState.lobby_members:
		members_by_cx[m.get("cxId", "")] = m

	var entries_arr: Array = []
	for e: Dictionary in entries:
		var member: Dictionary = members_by_cx.get(e["cx_id"], {})
		var profile_id: String = member.get("profileId", "")
		if profile_id.is_empty(): # can't post server-side without a profileId
			continue
		entries_arr.append({
			"profileId": profile_id,
			"name": member.get("name", "?"),
			"points": int(e["beaten"]) + 1,
			"coverageBasisPoints": int(e["coverage_pct"] * 100 + 0.5),
		})

	var payload := {
		"round": round,
		"lobbyId": AppState.lobby_id, # so the script can persist a "<lobbyId>:<round>"-indexed GlobalEntity for non-host clients to poll (see _tick_match_results_poll)
		"pointsLeaderboardId": AppState.points_leaderboard_id,
		"pointsLeaderboardIdQuarterly": AppState.points_leaderboard_id_quarterly,
		"coverageLeaderboardId": AppState.coverage_leaderboard_id,
		"coverageLeaderboardIdQuarterly": AppState.coverage_leaderboard_id_quarterly,
		"entries": entries_arr,
	}

	var result: Dictionary = await AppState.bc.script_service.run_script("PostMatchResults", payload)
	if result.get("status", 0) != 200:
		print("PostMatchResults script call failed for round %d: %s" % [round, result])
		return
	# The script's own return value is nested under data.response (a sibling of
	# runTimeData/success), not data itself — data.results is always empty/missing.
	_apply_leaderboard_results_from_cloud(round, result.get("data", {}).get("response", {}).get("results", []))

# Applies a PostMatchResults response (keyed by profileId) onto AppState.match_result_entries
# (keyed by cxId — resolved via lobby members) and refreshes the Match Summary screen if it's
# the one currently showing. Called on the host directly from _host_post_match_results_to_cloud's
# own script response, and on every other client from _tick_match_results_poll once the
# GlobalEntity that call writes shows up — both feed it the exact same "results" array shape,
# so there's only one place that parses it.
func _apply_leaderboard_results_from_cloud(round: int, results: Array) -> void:
	if AppState.match_result_round != round: # a newer round has already started
		return
	if results.is_empty():
		return

	var profile_to_cx: Dictionary = {}
	for m: Dictionary in AppState.lobby_members:
		var pid: String = m.get("profileId", "")
		if not pid.is_empty():
			profile_to_cx[pid] = m.get("cxId", "")

	for r: Dictionary in results:
		var profile_id: String = r.get("profileId", "")
		if profile_id.is_empty() or not profile_to_cx.has(profile_id):
			continue
		var cx: String = profile_to_cx[profile_id]
		var delta := {
			"ready": true,
			"points_lifetime":    _read_leaderboard_period(r.get("pointsLifetime", {})),
			"points_quarterly":   _read_leaderboard_period(r.get("pointsQuarterly", {})),
			"coverage_lifetime":  _read_leaderboard_period(r.get("coverageLifetime", {})),
			"coverage_quarterly": _read_leaderboard_period(r.get("coverageQuarterly", {})),
		}
		for e: Dictionary in AppState.match_result_entries:
			if e["cx_id"] == cx:
				e["lb_delta"] = delta
				break

	if _current_screen and _current_screen.has_method("refresh_results"):
		_current_screen.refresh_results(AppState.match_result_entries, AppState.lobby_members, AppState.user_cx_id)

static func _read_leaderboard_period(j: Dictionary) -> Dictionary:
	return {
		"improved": bool(j.get("improved", false)),
		"rank_before": int(j.get("before", -1)),
		"rank_after": int(j.get("after", -1)),
	}

# Non-host: polls the GlobalEntity PostMatchResults.js writes (indexed by "<lobbyId>:<round>")
# until it shows up, instead of waiting on the host to relay its own script response over the
# relay connection — which is usually already torn down by the time results would arrive
# (_teardown_relay runs as soon as we reach Match Summary), so the old "lb_result" broadcast
# could only ever land in a narrow window. Called from _process every frame; no-ops until
# there's a valid, not-yet-resolved match result for a non-host client, then self-throttles to
# _RESULTS_POLL_INTERVAL_SEC.
func _tick_match_results_poll(delta: float) -> void:
	if AppState.match_result_round < 0 or AppState.match_result_entries.is_empty():
		return
	if AppState.user_cx_id == AppState.lobby_owner_cx_id:
		return # host already has its results from its own PostMatchResults call

	for e: Dictionary in AppState.match_result_entries:
		if e.get("lb_delta", {}).get("ready", false):
			return # already applied

	if _results_poll_round != AppState.match_result_round:
		_results_poll_round = AppState.match_result_round
		_results_poll_accum = _RESULTS_POLL_INTERVAL_SEC # poll immediately on a fresh round
		_results_poll_in_flight = false
	if _results_poll_in_flight:
		return

	_results_poll_accum += delta
	if _results_poll_accum < _RESULTS_POLL_INTERVAL_SEC:
		return
	_results_poll_accum = 0.0
	_results_poll_in_flight = true

	var round: int = AppState.match_result_round
	var indexed_id: String = AppState.lobby_id + ":" + str(round)
	var result: Dictionary = await AppState.bc.global_entity_service.get_list_by_indexed_id("matchResults", indexed_id, 1)
	_results_poll_in_flight = false
	if result.get("status", 0) != 200:
		return # next tick retries
	var entity_list: Array = result.get("data", {}).get("entityList", [])
	if entity_list.is_empty():
		return # not written yet — next tick retries
	var results: Array = entity_list[0].get("data", {}).get("results", [])
	_apply_leaderboard_results_from_cloud(round, results)

# ── Rematch flow ───────────────────────────────────────────────────────────────

# Marks this player queued for a rematch and takes them back to the Lobby screen — used both
# by the Match Summary screen's "Queue for Rematch" button and by its own per-player
# rematch-timeout auto-queue. Once every current lobby member (this app's "Start"/"Ready"
# button is the same server-side signal) has queued, the lobby fires STARTING for the next
# round the same way it did for the first — no separate host-side gate needed.
func _on_rematch_ready_changed(ready: bool) -> void:
	AppState.user_is_ready = ready
	if ready:
		_show_screen(_LOBBY_SCENE)

	var extra := {"colorIndex": AppState.my_color_index}
	if not AppState.ping_data.is_empty():
		extra["pings"] = AppState.ping_data
	var result: Dictionary = await AppState.bc.lobby_service.update_ready(AppState.lobby_id, ready, extra)
	_recover_if_lobby_gone(result)

func _on_match_summary_main_menu_requested() -> void:
	AppState.bc.lobby_service.leave_lobby(AppState.lobby_id)
	_tear_down_rtt()
	_show_screen(_MENU_SCENE)

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
	AppState.lobby_chat_history.clear()

# ── Loading overlay ───────────────────────────────────────────────────────────

func _show_loading(text: String) -> void:
	_loading_label.text = text
	_loading_overlay.show()
	_loading_overlay.move_to_front()

func _hide_loading() -> void:
	_loading_overlay.hide()
