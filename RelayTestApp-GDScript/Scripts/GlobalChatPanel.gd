# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Shared app-wide "Global" chat channel view, used from both the main-menu and lobby screens.
# It's a pure view — Main owns channel resolution, history, and the live RTT push subscription
# (via channel_connect + register_rtt_chat_callback) so they survive this panel getting torn
# down and recreated on every screen transition. Sending just re-emits a signal for Main to
# relay; new messages, edits, and deletes (including our own) come back via
# Main.add/update/remove_global_chat_message, pushed live over RTT — see
# knowledge-articles/01-chat.md.
class_name GlobalChatPanel
extends VBoxContainer

signal send_requested(text: String)

@onready var _scroll:       ScrollContainer = %Scroll
@onready var _messages:     VBoxContainer   = %Messages
@onready var _status_label: Label           = %StatusLabel
@onready var _input:        LineEdit        = %Input
@onready var _send_button:  Button          = %SendButton

# msg_id -> row, so an update/delete (keyed by msg_id) can find its row again. If a message
# already scrolled out of history it just won't be in here, and the call is a no-op — nothing
# on screen to change anyway.
var _rows_by_msg_id: Dictionary = {}

func _ready() -> void:
	_send_button.pressed.connect(_send)
	_input.text_submitted.connect(func(_t): _send())
	_status_label.text = "Connecting..."

# Called once by Main right after this panel is created, to replay the channel's history —
# already fetched (via channel_connect) before this panel instance existed, e.g. from the
# main menu, or a prior round's lobby screen.
func set_history(history: Array) -> void:
	_status_label.visible = history.is_empty()
	for child in _messages.get_children():
		child.queue_free()
	_rows_by_msg_id.clear()
	for m: Dictionary in history:
		_add_message_row(m.get("msg_id", ""), m.get("from_name", "Player"), m.get("text", ""))
	_scroll_to_bottom()

func add_message(msg_id: String, from_name: String, text: String) -> void:
	_status_label.hide()
	_add_message_row(msg_id, from_name, text)
	_scroll_to_bottom()

# Same wire shape as add_message (full message, edited content) — updates the existing row's
# text in place so it doesn't jump to the bottom of the scroll.
func update_message(msg_id: String, text: String) -> void:
	var row: HBoxContainer = _rows_by_msg_id.get(msg_id)
	if row == null:
		return
	(row.get_child(1) as Label).text = text

func remove_message(msg_id: String) -> void:
	var row: HBoxContainer = _rows_by_msg_id.get(msg_id)
	if row == null:
		return
	row.queue_free()
	_rows_by_msg_id.erase(msg_id)

func _add_message_row(msg_id: String, from_name: String, text: String) -> void:
	var row := HBoxContainer.new()

	var from_label := Label.new()
	from_label.text = from_name + ":"
	from_label.add_theme_color_override("font_color", Color(0.6, 0.75, 1.0))

	var text_label := Label.new()
	text_label.text = text
	text_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	text_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	row.add_child(from_label)
	row.add_child(text_label)
	_messages.add_child(row)

	if not msg_id.is_empty():
		_rows_by_msg_id[msg_id] = row

func _scroll_to_bottom() -> void:
	# Deferred so the scroll container has processed the new children's sizes first.
	call_deferred("_do_scroll_to_bottom")

func _do_scroll_to_bottom() -> void:
	_scroll.scroll_vertical = int(_scroll.get_v_scroll_bar().max_value)

func _send() -> void:
	var text := _input.text.strip_edges()
	if text.is_empty():
		return
	_input.text = ""
	send_requested.emit(text)
