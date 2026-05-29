# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# One row in the lobby member list.
extends HBoxContainer

@onready var _color_rect: ColorRect = $ColorRect
@onready var _name_label: Label     = $NameLabel
@onready var _ping_label: Label     = $PingLabel

var cx_id: String = ""

func setup(member: Dictionary) -> void:
	cx_id = member.get("cxId", "")
	_name_label.text = member.get("name", "Unknown")
	var extra: Dictionary = member.get("extra", {})
	set_color_index(int(extra.get("colorIndex", 0)))
	set_ready(member.get("isReady", false))

	# Show the member's best reported region ping (from extra.pings shared via update_ready)
	var pings: Dictionary = extra.get("pings", {})
	if pings.is_empty():
		_ping_label.text = "-- ms"
	else:
		var best_ms := 9999
		for r: String in pings.keys():
			var ms := int(pings[r])
			if ms < best_ms:
				best_ms = ms
		set_ping(best_ms)

func set_color_index(index: int) -> void:
	if index < AppState.color_palette.size():
		_color_rect.color = AppState.color_palette[index]

func set_ready(is_ready: bool) -> void:
	_name_label.modulate.a = 1.0 if is_ready else 0.55

func set_ping(ms: int) -> void:
	if ms < 0:
		_ping_label.text = "-- ms"
	elif ms > 999:
		_ping_label.text = "999+ ms"
	else:
		_ping_label.text = "%d ms" % ms
