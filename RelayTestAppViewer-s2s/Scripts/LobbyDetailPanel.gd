# Copyright 2026 bitHeads, Inc. All Rights Reserved.
class_name LobbyDetailPanel
extends VBoxContainer

## Shows a snapshot of one selected lobby (name, state, members + their colour choice) via
## GET_LOBBY_DATA, then subscribes to that lobby's system channel
## (S2SContext.get_chat_service().sys_channel_connect) so it refreshes live as members
## join/leave/change ready-state, and shows the channel's chat history/live messages —
## with zero relay connection and zero lobby membership.

# Now just a safety net — sys_channel_connect (confirmed working against the live
# server) delivers live updates via RTT push, so this rarely needs to fire.
const FALLBACK_REFRESH_SECS := 30.0
const MAX_CHAT_MESSAGES := 30

var _context: S2SContext = null
var _colors: Array = []
var _state_colors := LobbyStateColors.new()
var _current_lobby_id: String = ""
var _current_channel_id: String = ""
var _subscribed: bool = false

var _title: Label
var _state_dot: TextureRect
var _state_label: Label
var _play_button: Button
var _members_container: VBoxContainer
var _chat_list: ItemList
var _fallback_timer: Timer

func _ready() -> void:
	var title_row := HBoxContainer.new()
	add_child(title_row)

	_title = Label.new()
	_title.text = "Lobby Detail"
	_title.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	title_row.add_child(_title)

	# "Join" just links out to react_relaytestapp (the web client) — there's no
	# lobby-id deep link, the player finds/joins the lobby themselves from there. This
	# viewer only ever reads via S2S; it never joins/plays anything itself.
	_play_button = Button.new()
	_play_button.text = "Play in Browser"
	_play_button.visible = false
	_play_button.pressed.connect(func(): OS.shell_open(Ids.WEB_APP_URL))
	title_row.add_child(_play_button)

	var state_row := HBoxContainer.new()
	add_child(state_row)

	_state_dot = TextureRect.new()
	_state_dot.custom_minimum_size = Vector2(10, 10)
	_state_dot.visible = false
	state_row.add_child(_state_dot)

	_state_label = Label.new()
	_state_label.text = "Select a lobby to view details."
	state_row.add_child(_state_label)

	var members_title := Label.new()
	members_title.text = "Members"
	add_child(members_title)

	# Scrolled and height-capped — lobbies can have up to ~40 members, which would
	# otherwise push everything below (chat, and this whole panel's siblings in
	# Main.gd's right_vbox) out of view.
	var members_scroll := ScrollContainer.new()
	members_scroll.custom_minimum_size = Vector2(0, 100)
	members_scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(members_scroll)

	_members_container = VBoxContainer.new()
	_members_container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	members_scroll.add_child(_members_container)

	var chat_title := Label.new()
	chat_title.text = "Chat"
	add_child(chat_title)

	_chat_list = ItemList.new()
	_chat_list.custom_minimum_size = Vector2(0, 120)
	_chat_list.auto_height = false
	add_child(_chat_list)

	_fallback_timer = Timer.new()
	_fallback_timer.wait_time = FALLBACK_REFRESH_SECS
	_fallback_timer.autostart = false
	_fallback_timer.timeout.connect(func(): _refresh_snapshot())
	add_child(_fallback_timer)

func set_context(context: S2SContext) -> void:
	_context = context
	_colors = await GlobalProperties.fetch_colors(context)

func show_lobby(lobby_id: String) -> void:
	if lobby_id == _current_lobby_id:
		return

	await _unsubscribe_channel()
	_current_lobby_id = lobby_id
	_fallback_timer.stop()
	_state_label.text = "Loading..."
	_chat_list.clear()

	await _refresh_snapshot()
	await _subscribe_channel()
	_fallback_timer.start()

func clear() -> void:
	await _unsubscribe_channel()
	_current_lobby_id = ""
	_fallback_timer.stop()
	_title.text = "Lobby Detail"
	_state_label.text = "Select a lobby to view details."
	_state_dot.visible = false
	_play_button.visible = false
	_clear_members()
	_chat_list.clear()

func _refresh_snapshot() -> void:
	if _context == null or _current_lobby_id.is_empty():
		return

	var response := await _context.request({"service": "lobby", "operation": "GET_LOBBY_DATA", "data": {"lobbyId": _current_lobby_id}})
	if int(response.get("status", -1)) != 200:
		_state_label.text = "Lobby not found (it may have ended)."
		_state_dot.visible = false
		_play_button.visible = false
		_clear_members()
		return

	_render_snapshot(response.get("data", {}))

func _render_snapshot(data: Dictionary) -> void:
	var lobby_name := String(data.get("description", data.get("name", _current_lobby_id)))
	var state := String(data.get("state", "?"))
	var round_num = data.get("round", null)
	_title.text = "Lobby: %s" % lobby_name
	_state_label.text = "State: %s%s" % [state, ("  ·  Round: %d" % int(round_num)) if round_num != null else ""]
	_state_dot.texture = _state_colors.dot_texture_for(state)
	_state_dot.visible = true
	_play_button.visible = true

	_clear_members()
	var members: Array = data.get("members", [])
	for member in members:
		if typeof(member) == TYPE_DICTIONARY:
			_add_member_row(member)

func _add_member_row(member: Dictionary) -> void:
	var row := HBoxContainer.new()

	var extra: Dictionary = member.get("extra", {})
	var color_index := int(extra.get("colorIndex", -1))
	var swatch := ColorRect.new()
	swatch.custom_minimum_size = Vector2(16, 16)
	swatch.color = _colors[color_index] if (color_index >= 0 and color_index < _colors.size()) else Color.GRAY
	row.add_child(swatch)

	var member_name := String(member.get("name", member.get("profileId", "?")))
	var is_ready := bool(member.get("ready", member.get("isReady", false)))
	var name_label := Label.new()
	name_label.text = "%s%s" % [member_name, "  (ready)" if is_ready else ""]
	row.add_child(name_label)

	_members_container.add_child(row)

func _clear_members() -> void:
	for child in _members_container.get_children():
		child.queue_free()

func _subscribe_channel() -> void:
	if _context == null or _current_lobby_id.is_empty():
		return

	# sys_channel_connect ties the subscription to this S2S session's live RTT connection
	# — calling it before RTT has actually finished connecting risks a subscription with
	# nowhere to route pushes to. A lobby can be selected at any time relative to when
	# Main.gd's enable_rtt() callback fires, so wait here rather than assume it's ready.
	var rtt := _context.get_rtt_service()
	var deadline := Time.get_ticks_msec() + 5000
	while not rtt.is_enabled() and Time.get_ticks_msec() < deadline:
		await get_tree().process_frame
	if not rtt.is_enabled():
		push_warning("LobbyDetailPanel: RTT never connected — falling back to %ds polling only." % int(FALLBACK_REFRESH_SECS))
		return

	var chat := _context.get_chat_service()
	_current_channel_id = chat.build_lobby_system_channel_id(_current_lobby_id)
	var response := await chat.sys_channel_connect(_current_channel_id, MAX_CHAT_MESSAGES)
	_subscribed = int(response.get("status", -1)) == 200

	if _subscribed:
		_context.get_rtt_service().register_raw_callback(_on_rtt_message)
		var messages: Array = response.get("data", {}).get("messages", [])
		for message in messages:
			if typeof(message) == TYPE_DICTIONARY:
				_add_message_from_content(message.get("content", {}))
	else:
		push_warning("LobbyDetailPanel: sys_channel_connect failed for %s — falling back to %ds polling only." % [_current_channel_id, int(FALLBACK_REFRESH_SECS)])

func _unsubscribe_channel() -> void:
	if not _subscribed:
		return
	_context.get_rtt_service().deregister_raw_callback(_on_rtt_message)
	await _context.get_chat_service().sys_channel_disconnect(_current_channel_id)
	_subscribed = false
	_current_channel_id = ""

## RTT push envelope, confirmed via a live capture:
##   {"service":"chat","operation":"INCOMING","data":{"chId":...,
##    "content":{"op":"SIGNAL","data":{"from":{"name","cxId",...},"signalData":{"text":...}}}}}
## "This lobby" chat is implemented via the Lobby service's SendSignal, not a genuine
## Chat-service message (see cpp-examples/relaytestapp/src/app.cpp:1936-1961) — it only
## arrives here because SharedLobbyService broadcasts lobby signals onto the same system
## channel this S2S session is subscribed to. Any other content.op (member join/leave/
## ready/settings/starting/etc.) triggers a snapshot refresh instead — a signal doesn't
## change lobby/member state, so no need to re-fetch for it.
func _on_rtt_message(msg: Dictionary) -> void:
	if String(msg.get("service", "")) != "chat" or String(msg.get("operation", "")) != "INCOMING":
		return

	var content: Dictionary = msg.get("data", {}).get("content", {})
	if not _add_message_from_content(content):
		_refresh_snapshot()

## Returns true if `content` was a recognized lobby signal (and was rendered into the chat
## log) — false for anything else (caller falls back to a full snapshot refresh).
func _add_message_from_content(content: Dictionary) -> bool:
	if String(content.get("op", "")) != "SIGNAL":
		return false

	var signal_data: Dictionary = content.get("data", {})
	var from_name := String(signal_data.get("from", {}).get("name", "?"))
	var text := String(signal_data.get("signalData", {}).get("text", ""))
	if text.is_empty():
		return true # recognized as a signal, just nothing to show (e.g. non-chat signal)

	_chat_list.add_item("%s: %s" % [from_name, text])
	while _chat_list.item_count > MAX_CHAT_MESSAGES:
		_chat_list.remove_item(0)
	_chat_list.ensure_current_is_visible()
	return true
