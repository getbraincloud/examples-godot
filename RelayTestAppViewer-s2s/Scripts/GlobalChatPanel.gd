# Copyright 2026 bitHeads, Inc. All Rights Reserved.
class_name GlobalChatPanel
extends VBoxContainer

## Read-only view of the app-wide "gl" global chat channel (see
## cpp-examples/relaytestapp/src/globalChat.cpp:25,69-70 for the ("gl","gl") type/sub-id
## convention every RTA client shares). Connects once RTT is up and stays connected for
## the app's lifetime — global chat isn't tied to any particular lobby selection.

const MAX_MESSAGES := 30

var _context: S2SContext = null
var _channel_id: String = ""
var _subscribed: bool = false

var _status_label: Label
var _list: ItemList

func _ready() -> void:
	var title := Label.new()
	title.text = "Global Chat"
	add_child(title)

	_status_label = Label.new()
	_status_label.text = ""
	add_child(_status_label)

	_list = ItemList.new()
	_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(_list)

func set_context(context: S2SContext) -> void:
	_context = context
	_connect.call_deferred()

func _connect() -> void:
	if _context == null:
		return

	var rtt := _context.get_rtt_service()
	var deadline := Time.get_ticks_msec() + 5000
	while not rtt.is_enabled() and Time.get_ticks_msec() < deadline:
		await get_tree().process_frame
	if not rtt.is_enabled():
		_status_label.text = "RTT not connected — global chat unavailable."
		return

	var chat := _context.get_chat_service()
	var id_response := await chat.get_channel_id("gl", "gl")
	if int(id_response.get("status", -1)) != 200:
		_status_label.text = "Could not resolve global channel."
		return
	_channel_id = String(id_response.get("data", {}).get("channelId", ""))
	if _channel_id.is_empty():
		_status_label.text = "Could not resolve global channel."
		return

	var connect_response := await chat.sys_channel_connect(_channel_id, MAX_MESSAGES)
	_subscribed = int(connect_response.get("status", -1)) == 200
	if not _subscribed:
		_status_label.text = "Could not connect to global channel."
		return

	rtt.register_raw_callback(_on_rtt_message)
	_status_label.text = ""
	var messages: Array = connect_response.get("data", {}).get("messages", [])
	for message in messages:
		if typeof(message) == TYPE_DICTIONARY:
			_add_message(message)

func _on_rtt_message(msg: Dictionary) -> void:
	if String(msg.get("service", "")) != "chat" or String(msg.get("operation", "")) != "INCOMING":
		return
	var data: Dictionary = msg.get("data", {})
	if String(data.get("chId", "")) != _channel_id:
		return # a push for some other channel this session happens to be subscribed to
	_add_message(data)

## Genuine posted Chat-service messages (unlike lobby signals) use the flat shape
## documented for SysChannelConnect: {"from":{"name"}, "content":{"text"}}.
func _add_message(message: Dictionary) -> void:
	var from_name := String(message.get("from", {}).get("name", "?"))
	var text := String(message.get("content", {}).get("text", ""))
	if text.is_empty():
		return
	_list.add_item("%s: %s" % [from_name, text])
	while _list.item_count > MAX_MESSAGES:
		_list.remove_item(0)
	_list.ensure_current_is_visible()
