# Copyright 2026 bitHeads, Inc. All Rights Reserved.
class_name BrainCloudS2SRTT
extends RefCounted

## S2S real-time tech (RTT) relay comms. Obtain an instance via [method S2SContext.get_rtt_service],
## or just call [method S2SContext.enable_rtt].
##
## [WebSocketPeer] has no signals, so unlike the rest of this library this service is
## poll-driven: call [method poll] every frame (or on whatever cadence your server's
## main loop allows) once [method enable] has been called. [method poll] drives the
## connection handshake, dispatches incoming messages to the raw callback, and sends
## the periodic heartbeat.

enum ConnectionState { DISCONNECTED, CONNECTING, HANDSHAKING, CONNECTED }

const PLATFORM := "GODOT_S2S"
const DEFAULT_HEARTBEAT_SECS := 600.0

var _context: S2SContext
var _socket: WebSocketPeer = null
var _connection_state: int = ConnectionState.DISCONNECTED
var _connect_callback: Callable = Callable()
var _raw_callbacks: Array[Callable] = []
var _app_id: String = ""
var _session_id: String = ""
var _auth: Dictionary = {}
var _heartbeat_interval_secs: float = DEFAULT_HEARTBEAT_SECS
var _last_heartbeat_ms: int = 0

## Fires once per enable() attempt (success or failure) — an alternative to the
## `callback` param for callers that prefer `await rtt.connect_result`.
signal connect_result(success: bool, result: Dictionary)
signal disconnected_with_reason(reason: Dictionary)

func _init(context: S2SContext) -> void:
	_context = context

func is_enabled() -> bool:
	return _connection_state == ConnectionState.CONNECTED

## Registers a callback for all non-"rtt"-service RTT messages (i.e. relay/game events,
## or chat-channel pushes). Multiple independent callbacks can be registered at once
## (e.g. one per subscribed chat channel) — every push goes to all of them.
func register_raw_callback(callback: Callable) -> void:
	if not _raw_callbacks.has(callback):
		_raw_callbacks.append(callback)

## Removes one previously-registered callback (only that one — other subscribers, e.g.
## another chat channel's listener, are unaffected). Safe to call with a callback that
## was never registered (a no-op), so callers don't need to track whether they're
## currently registered.
func deregister_raw_callback(callback: Callable) -> void:
	_raw_callbacks.erase(callback)

## Requests an RTT endpoint via S2S, then opens a WebSocket connection to it.
## `callback`, if given, is invoked once with (success: bool, result: Dictionary) once
## the connection is established (or fails at any step).
func enable(callback: Callable = Callable()) -> void:
	if _connection_state != ConnectionState.DISCONNECTED:
		return

	_connect_callback = callback
	_connection_state = ConnectionState.CONNECTING

	var result := await _context.request({
		"service": "rttRegistration",
		"operation": "REQUEST_SYSTEM_CONNECTION",
		"data": {},
	})

	if _connection_state != ConnectionState.CONNECTING:
		return  # disable() was called while the S2S request was in flight

	if int(result.get("status", 0)) != 200:
		_connection_state = ConnectionState.DISCONNECTED
		_invoke_connect_callback(false, result)
		return

	var endpoints: Array = result.get("data", {}).get("endpoints", [])
	var chosen: Dictionary = {}
	for endpoint in endpoints:
		if String(endpoint.get("protocol", "")) == "ws":
			chosen = endpoint
			break

	if chosen.is_empty():
		_connection_state = ConnectionState.DISCONNECTED
		_invoke_connect_callback(false, {"status": 0, "status_message": "WebSocket endpoint missing"})
		return

	_app_id = _context.get_app_id()
	_session_id = _context.get_session_id()
	_auth = result.get("data", {}).get("auth", {})

	var scheme := "wss" if bool(chosen.get("ssl", false)) else "ws"
	var url := "%s://%s:%d/" % [scheme, String(chosen.get("host", "")), int(chosen.get("port", 0))]
	# The RTT gateway authorizes the handshake itself off these query params — the
	# CONNECT frame sent once the socket is open (see _send_connect_request) carries
	# the same auth again for the application-level handshake.
	if not _auth.is_empty():
		var parts: PackedStringArray = []
		for key in _auth.keys():
			parts.append("%s=%s" % [str(key).uri_encode(), str(_auth[key]).uri_encode()])
		url += "?" + "&".join(parts)

	_socket = WebSocketPeer.new()
	var err := _socket.connect_to_url(url)
	if err != OK:
		_socket = null
		_connection_state = ConnectionState.DISCONNECTED
		_invoke_connect_callback(false, {"status": 0, "status_message": "connect_to_url failed: %d" % err})
		return

	# Handshake continues in poll(): once the socket opens we send the rtt CONNECT
	# request, and the server's CONNECT response flips us to CONNECTED.

## Drives the connection handshake, message dispatch, and heartbeat. Call every frame
## (or your server's equivalent tick) once enable() has been called.
func poll() -> void:
	if _socket == null:
		return

	_socket.poll()
	var state := _socket.get_ready_state()

	if state == WebSocketPeer.STATE_OPEN:
		if _connection_state == ConnectionState.CONNECTING:
			_send_connect_request()
			_connection_state = ConnectionState.HANDSHAKING

		while _socket.get_available_packet_count() > 0:
			_handle_packet(_socket.get_packet())

		if _connection_state == ConnectionState.CONNECTED:
			_update_heartbeat()

	elif state == WebSocketPeer.STATE_CLOSED:
		if _connection_state != ConnectionState.DISCONNECTED:
			var was_connecting := _connection_state != ConnectionState.CONNECTED
			_socket = null
			_connection_state = ConnectionState.DISCONNECTED
			if was_connecting:
				_invoke_connect_callback(false, {"status": 0, "status_message": "closed"})

## Closes the RTT connection, if any.
func disable() -> void:
	if _socket != null:
		_socket.close()
		_socket = null
	_connection_state = ConnectionState.DISCONNECTED
	_connect_callback = Callable()

func _send_connect_request() -> void:
	var request := {
		"operation": "CONNECT",
		"service": "rtt",
		"data": {
			"appId": _app_id,
			"profileId": "s",
			"sessionId": _session_id,
			"auth": _auth,
			"system": {
				"protocol": "ws",
				"platform": PLATFORM,
			},
		},
	}
	_log_wire("WS SEND", JSON.stringify(request))
	_socket.send_text(JSON.stringify(request))

func _handle_packet(packet: PackedByteArray) -> void:
	var text := packet.get_string_from_utf8()
	var json := JSON.new()
	if json.parse(text) != OK or typeof(json.get_data()) != TYPE_DICTIONARY:
		if _context.get_log_enabled():
			print("WS RECV parse error, data=%s" % text)
		return
	var parsed: Dictionary = json.get_data()

	_log_wire("WS RECV", text)

	if String(parsed.get("service", "")) != "rtt":
		# Iterate a copy — a callback that deregisters itself (or another) mid-dispatch
		# must not corrupt this loop.
		for callback in _raw_callbacks.duplicate():
			if callback.is_valid():
				callback.call(parsed)
		return

	match String(parsed.get("operation", "")):
		"CONNECT":
			var data: Dictionary = parsed.get("data", {})
			_heartbeat_interval_secs = float(data.get("heartbeatSeconds", DEFAULT_HEARTBEAT_SECS))
			_last_heartbeat_ms = Time.get_ticks_msec()
			_connection_state = ConnectionState.CONNECTED
			_invoke_connect_callback(true, parsed)
		"DISCONNECT":
			var data: Dictionary = parsed.get("data", {})
			disconnected_with_reason.emit({
				"severity": "ERROR",
				"reason": data.get("reason", ""),
				"reasonCode": data.get("reasonCode", 0),
			})

func _update_heartbeat() -> void:
	var now := Time.get_ticks_msec()
	if now - _last_heartbeat_ms >= int(_heartbeat_interval_secs * 1000.0):
		_last_heartbeat_ms = now
		var request := {"operation": "HEARTBEAT", "service": "rtt", "data": null}
		_log_wire("WS SEND", JSON.stringify(request))
		_socket.send_text(JSON.stringify(request))

func _log_wire(prefix: String, text: String) -> void:
	if not _context.get_log_enabled():
		return
	print("%s: %s" % [prefix, text if _context.get_show_secret_logs() else S2SContext.redact(text)])

func _invoke_connect_callback(success: bool, result: Dictionary) -> void:
	var cb := _connect_callback
	_connect_callback = Callable()
	if cb.is_valid():
		cb.call(success, result)
	connect_result.emit(success, result)
