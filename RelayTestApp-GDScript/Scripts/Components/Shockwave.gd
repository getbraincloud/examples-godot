# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Expanding ring effect spawned on click. Fades out over 1 second.
extends Node2D

const _LIFETIME    := 1.0
const _MAX_RADIUS  := 80.0
const _LINE_WIDTH  := 3.0

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
	var radius := _MAX_RADIUS * ease(t, -1.7)  # ease-out expansion
	var alpha  := 1.0 - t
	draw_arc(Vector2.ZERO, radius, 0.0, TAU, 48, Color(_color, alpha), _LINE_WIDTH)
