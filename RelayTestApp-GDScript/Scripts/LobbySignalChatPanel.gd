# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# This-lobby chat, via the Lobby service's send_signal (not the Chat service — it rides the
# RTT connection the lobby already has, so no separate channel is needed). Unlike
# GlobalChatPanel, this panel makes no brainCloud calls of its own: Main owns the message
# history (so it survives this screen being recreated between rounds) and pushes rows in via
# add_message/clear; sending just re-emits a signal for Main to relay.
class_name LobbySignalChatPanel
extends VBoxContainer

signal send_requested(text: String)

@onready var _scroll:      ScrollContainer = %Scroll
@onready var _messages:    VBoxContainer   = %Messages
@onready var _input:       LineEdit        = %Input
@onready var _send_button: Button          = %SendButton

func _ready() -> void:
	_send_button.pressed.connect(_send)
	_input.text_submitted.connect(func(_t): _send())

func clear() -> void:
	for child in _messages.get_children():
		child.queue_free()

func add_message(from_name: String, text: String, is_me: bool) -> void:
	var row := HBoxContainer.new()

	var from_label := Label.new()
	from_label.text = from_name + ":"
	from_label.add_theme_color_override("font_color", Color(0.35, 1.0, 0.45) if is_me else Color(0.6, 0.75, 1.0))

	var text_label := Label.new()
	text_label.text = text
	text_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	text_label.size_flags_horizontal = Control.SIZE_EXPAND_FILL

	row.add_child(from_label)
	row.add_child(text_label)
	_messages.add_child(row)

	call_deferred("_scroll_to_bottom")

func _scroll_to_bottom() -> void:
	_scroll.scroll_vertical = int(_scroll.get_v_scroll_bar().max_value)

func _send() -> void:
	var text := _input.text.strip_edges()
	if text.is_empty():
		return
	_input.text = ""
	send_requested.emit(text)
