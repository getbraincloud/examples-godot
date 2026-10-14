# Copyright 2026 bitHeads, Inc. All Rights Reserved.
class_name S2SContext
extends Node

## brainCloud Server-to-Server (S2S) request/auth context.
##
## Create with [method create] (which adds itself to the scene tree for you), then either:
##   [codeblock]
##   var result := await context.authenticate()
##   [/codeblock]
## or, for parity with the callback style used by the other S2S SDKs:
##   [codeblock]
##   context.authenticate(func(result): ...)
##   [/codeblock]
##
## Only one authenticate()/request() is ever in flight at a time — a second call made
## before the first resolves waits its turn. This is a simpler equivalent of the explicit
## request queue the C++/Java/JS S2S SDKs keep, made possible by GDScript's coroutines.

const S2S_VERSION := "1.0.0"
const DEFAULT_S2S_URL := "https://api.braincloudservers.com/s2sdispatcher"

const SERVER_SESSION_EXPIRED := 40365
const HEARTBEAT_INTERVAL_SECS := 60.0 * 30.0
const MAX_REAUTH_RETRIES := 3

const SENSITIVE_KEYS := ["secretKey", "serverSecret", "ApiKey", "secret", "token", "X-RTT-SECRET"]

enum State { DISCONNECTED, AUTHENTICATING, CONNECTED }

var _app_id: String = ""
var _server_name: String = ""
var _server_secret: String = ""
var _url: String = ""
var _auto_auth: bool = false
var _session_id: String = ""
var _packet_id: int = 0
var _state: int = State.DISCONNECTED
var _log_enabled: bool = false
var _show_secret_logs: bool = false
var _retry_count: int = 0
var _busy: bool = false
var _heartbeat_timer: Timer = null

# Keep-alive transport. 
var _http: HTTPClient = null
var _http_host: String = ""
var _http_port: int = -1
var _http_tls: bool = false
var _http_path: String = "/"

var _global_file_v3: BrainCloudS2SGlobalFileV3 = null
var _rtt: BrainCloudS2SRTT = null
var _chat: BrainCloudS2SChat = null

signal _turn_available

## Create a new S2S context and add it to the scene tree.
## @param app_id Application ID
## @param server_name Server name
## @param server_secret Server secret key
## @param url The server url to send requests to. Empty string = DEFAULT_S2S_URL.
## @param auto_auth If true, the context authenticates itself on the first request()
##                   call if it hasn't already. If false (recommended), call
##                   authenticate() yourself and wait for a successful result first.
## @param owner Node the context is added as a child of. Defaults to the main loop's
##              root, so the context stays alive for the life of the process.
static func create(app_id: String, server_name: String, server_secret: String,
		url: String = "", auto_auth: bool = false, owner: Node = null) -> S2SContext:
	var ctx := S2SContext.new()
	ctx._app_id = app_id
	ctx._server_name = server_name
	ctx._server_secret = server_secret
	ctx._url = url if url.length() > 0 else DEFAULT_S2S_URL
	ctx._auto_auth = auto_auth

	var parent: Node = owner
	if parent == null:
		parent = Engine.get_main_loop().root
	parent.add_child(ctx)

	return ctx

func get_app_id() -> String:
	return _app_id

func get_server_name() -> String:
	return _server_name

func get_server_secret() -> String:
	return _server_secret

func get_server_url() -> String:
	return _url

func get_session_id() -> String:
	return _session_id

func get_s2s_version() -> String:
	return S2S_VERSION

func is_authenticated() -> bool:
	return _state == State.CONNECTED

func get_log_enabled() -> bool:
	return _log_enabled

func set_log_enabled(enabled: bool) -> void:
	_log_enabled = enabled

func get_show_secret_logs() -> bool:
	return _show_secret_logs

func set_show_secret_logs(enabled: bool) -> void:
	_show_secret_logs = enabled

func get_global_file_v3() -> BrainCloudS2SGlobalFileV3:
	if _global_file_v3 == null:
		_global_file_v3 = BrainCloudS2SGlobalFileV3.new(self)
	return _global_file_v3

func get_rtt_service() -> BrainCloudS2SRTT:
	if _rtt == null:
		_rtt = BrainCloudS2SRTT.new(self)
	return _rtt

func get_chat_service() -> BrainCloudS2SChat:
	if _chat == null:
		_chat = BrainCloudS2SChat.new(self)
	return _chat

## Attempt to establish an RTT connection. `callback`, if given, is invoked with
## (connected: bool, result: Dictionary) once the attempt succeeds or fails. Once
## connected, call get_rtt_service().poll() every frame to keep the connection alive
## and dispatch incoming messages (WebSocketPeer has no signals — it must be polled).
func enable_rtt(callback: Callable = Callable()) -> void:
	get_rtt_service().enable(callback)

func disable_rtt() -> void:
	if _rtt != null:
		_rtt.disable()

func disconnect_context() -> void:
	_stop_heartbeat()
	_http_close()
	if _rtt != null:
		_rtt.disable()
	_state = State.DISCONNECTED
	_retry_count = 0
	_packet_id = 0
	_session_id = ""

## Authenticate with brainCloud. Must be called (and awaited/succeed) before request()
## unless `auto_auth` was passed to create().
func authenticate(callback: Callable = Callable()) -> Dictionary:
	while _busy:
		await _turn_available
	_busy = true

	var result := await _authenticate_internal()

	_busy = false
	_turn_available.emit()

	_invoke(callback, result)
	return result

## Send an S2S request, e.g. { "service": "globalFileV3", "operation": "SYS_GET_FILE_INFO", "data": {...} }.
func request(data: Dictionary, callback: Callable = Callable()) -> Dictionary:
	while _busy:
		await _turn_available
	_busy = true

	if _state == State.DISCONNECTED and _auto_auth:
		await _authenticate_internal()

	var result := await _send_request(data)

	if int(result.get("status", -1)) != 200 \
			and int(result.get("reason_code", 0)) == SERVER_SESSION_EXPIRED \
			and _retry_count < MAX_REAUTH_RETRIES:
		_retry_count += 1
		_stop_heartbeat()
		_state = State.DISCONNECTED
		_packet_id = 0
		_session_id = ""
		await _authenticate_internal()
		result = await _send_request(data)
	else:
		_retry_count = 0

	_busy = false
	_turn_available.emit()

	_invoke(callback, result)
	return result

func _authenticate_internal() -> Dictionary:
	_state = State.AUTHENTICATING

	var packet := {
		"packetId": 0,
		"messages": [
			{
				"service": "authenticationV2",
				"operation": "AUTHENTICATE",
				"data": {
					"appId": _app_id,
					"serverName": _server_name,
					"serverSecret": _server_secret,
				}
			}
		]
	}

	var response := await _post(packet)
	var message := _first_message(response)

	if int(message.get("status", 0)) == 200:
		_state = State.CONNECTED
		_packet_id = int(response.get("packetId", 0)) + 1
		_session_id = String(message.get("data", {}).get("sessionId", ""))
		_start_heartbeat()
	else:
		_state = State.DISCONNECTED

	return message

func _send_request(data: Dictionary) -> Dictionary:
	var packet := {
		"packetId": _packet_id,
		"sessionId": _session_id,
		"messages": [data],
	}
	_packet_id += 1

	var response := await _post(packet)
	return _first_message(response)

func _first_message(response: Dictionary) -> Dictionary:
	var responses: Array = response.get("messageResponses", [])
	if responses.size() > 0:
		return responses[0]
	return response

func _post(packet: Dictionary) -> Dictionary:
	var body := JSON.stringify(packet)

	if _log_enabled:
		print("[S2S SEND %s] %s" % [_app_id, body if _show_secret_logs else redact(body)])

	var headers := PackedStringArray(["Content-Type: application/json"])
	var text := await _send_over_shared_connection(headers, body.to_utf8_buffer(), true)
	if text.is_empty():
		# Either a transport failure (already logged) or a genuinely empty body; both
		# end up as an empty result below.
		return {}

	if _log_enabled:
		print("[S2S RECV %s] %s" % [_app_id, text if _show_secret_logs else redact(text)])

	if text.is_empty():
		return {}

	var json := JSON.new()
	if json.parse(text) != OK or typeof(json.get_data()) != TYPE_DICTIONARY:
		if _log_enabled:
			print("[S2S Error parsing response data %s]" % _app_id)
		return {}

	return json.get_data()

# ── Keep-alive transport ────────────────────────────────────────────────────

func _http_parse_url() -> void:
	_http_tls = _url.begins_with("https://")
	var rest := _url
	var scheme_end := _url.find("://")
	if scheme_end >= 0:
		rest = _url.substr(scheme_end + 3)
	var slash := rest.find("/")
	var host_port := rest if slash < 0 else rest.substr(0, slash)
	_http_path = "/" if slash < 0 else rest.substr(slash)
	_http_port = 443 if _http_tls else 80
	var colon := host_port.rfind(":")
	if colon > 0:
		_http_port = int(host_port.substr(colon + 1))
		host_port = host_port.substr(0, colon)
	_http_host = host_port

func _http_close() -> void:
	if _http != null:
		_http.close()

# Returns true once the shared connection is usable. Reuses the existing socket when
# one is already open, which is the entire point.
func _http_connect() -> bool:
	var prev_host := _http_host
	var prev_port := _http_port
	_http_parse_url()
	if _http != null and (_http_host != prev_host or _http_port != prev_port):
		_http_close()
	if _http == null:
		_http = HTTPClient.new()
	if _http.get_status() == HTTPClient.STATUS_CONNECTED:
		return true

	var tls_options: TLSOptions = TLSOptions.client() if _http_tls else null
	var err := _http.connect_to_host(_http_host, _http_port, tls_options)
	if err != OK:
		if _log_enabled:
			print("[S2S Error connecting %s] connect_to_host failed: %d" % [_app_id, err])
		return false

	while _http.get_status() == HTTPClient.STATUS_RESOLVING or _http.get_status() == HTTPClient.STATUS_CONNECTING:
		_http.poll()
		await get_tree().process_frame

	if _http.get_status() != HTTPClient.STATUS_CONNECTED:
		if _log_enabled:
			print("[S2S Error connecting %s] status=%d" % [_app_id, _http.get_status()])
		_http_close()
		return false
	return true

# One request over the shared connection. `allow_retry` covers the normal case of the
# server having reaped an idle keep-alive socket between calls: that is routine, not an
# error, so it reconnects and sends once more before giving up.
func _send_over_shared_connection(headers: PackedStringArray, body_bytes: PackedByteArray, allow_retry: bool) -> String:
	if not await _http_connect():
		return ""

	var err := _http.request_raw(HTTPClient.METHOD_POST, _http_path, headers, body_bytes)
	if err != OK:
		_http_close()
		if allow_retry:
			return await _send_over_shared_connection(headers, body_bytes, false)
		if _log_enabled:
			print("[S2S Error making request %s] request_raw failed: %d" % [_app_id, err])
		return ""

	while _http.get_status() == HTTPClient.STATUS_REQUESTING:
		_http.poll()
		await get_tree().process_frame

	if not _http.has_response():
		# Socket went away before any headers arrived - almost always a reaped idle
		# keep-alive connection.
		_http_close()
		if allow_retry:
			return await _send_over_shared_connection(headers, body_bytes, false)
		if _log_enabled:
			print("[S2S Error making request %s] no response (status=%d)" % [_app_id, _http.get_status()])
		return ""

	var chunks := PackedByteArray()
	while _http.get_status() == HTTPClient.STATUS_BODY:
		_http.poll()
		var chunk := _http.read_response_body_chunk()
		if chunk.size() > 0:
			chunks.append_array(chunk)
		else:
			await get_tree().process_frame

	return chunks.get_string_from_utf8()
func _start_heartbeat() -> void:
	_stop_heartbeat()
	_heartbeat_timer = Timer.new()
	_heartbeat_timer.wait_time = HEARTBEAT_INTERVAL_SECS
	_heartbeat_timer.one_shot = false
	_heartbeat_timer.timeout.connect(_on_heartbeat)
	add_child(_heartbeat_timer)
	_heartbeat_timer.start()

func _stop_heartbeat() -> void:
	if _heartbeat_timer != null:
		_heartbeat_timer.stop()
		_heartbeat_timer.queue_free()
		_heartbeat_timer = null

func _on_heartbeat() -> void:
	var result := await request({"service": "heartbeat", "operation": "HEARTBEAT"})
	if int(result.get("status", 0)) != 200:
		disconnect_context()

func _invoke(callback: Callable, result: Dictionary) -> void:
	if callback.is_valid():
		callback.call(result)

static func redact(text: String) -> String:
	var out := text
	for key in SENSITIVE_KEYS:
		var needle := "\"%s\":" % key
		var idx := out.find(needle)
		while idx >= 0:
			var pos := idx + needle.length()
			while pos < out.length() and out[pos] != "\"":
				pos += 1
			if pos >= out.length():
				break
			var value_start := pos + 1
			var value_end := out.find("\"", value_start)
			if value_end < 0:
				break
			out = out.substr(0, value_start) + "[REDACTED]" + out.substr(value_end)
			idx = out.find(needle, value_start + 10)
	return out
