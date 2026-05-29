# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Expanding filled-circle impact effect. Matches C++ RelayTestApp visual.
extends Node2D

const _LIFETIME   := 1.0
const _MAX_RADIUS := 64.0

var _color:   Color = Color.WHITE
var _elapsed: float = 0.0

func setup(color: Color) -> void:
	_color = color

func _process(delta: float) -> void:
	_elapsed += delta
	if _elapsed >= _LIFETIME:
		queue_free()
		return
	queue_redraw()

func _draw() -> void:
	var t      := _elapsed / _LIFETIME
	var ease_t := 1.0 - (1.0 - t) * (1.0 - t)  # ease-out
	var radius := _MAX_RADIUS * ease_t
	var alpha  := 1.0 - t
	draw_circle(Vector2.ZERO, radius, Color(_color, alpha))
