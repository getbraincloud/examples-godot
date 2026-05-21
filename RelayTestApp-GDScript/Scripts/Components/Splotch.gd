# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Persistent paint mark left at the shockwave origin.
# Grows with a spring-overshoot pop, then fades when SplotchDuration expires.
extends Node2D

const _SPLAT_ANIM_TIME := 0.3   # seconds for the pop animation
const _BASE_RADIUS     := 16.0
const _FADE_SECS       := 3.0   # seconds over which the splotch fades before removal

var _color:    Color = Color.WHITE
var _elapsed:  float = 0.0
var _duration: int   = -1  # -1 = forever, otherwise seconds

func setup(color: Color, duration: int) -> void:
	_color    = Color(
		clampf(color.r + randf_range(-0.07, 0.07), 0, 1),
		clampf(color.g + randf_range(-0.07, 0.07), 0, 1),
		clampf(color.b + randf_range(-0.07, 0.07), 0, 1)
	)
	_duration = duration

func _process(delta: float) -> void:
	_elapsed += delta
	if _duration > 0 and _elapsed >= float(_duration):
		queue_free()
		return
	queue_redraw()

func _draw() -> void:
	var t     := clampf(_elapsed / _SPLAT_ANIM_TIME, 0.0, 1.0)
	var size  := _splat_size(t) * _BASE_RADIUS
	var alpha := 1.0
	if _duration > 0:
		var time_left := float(_duration) - _elapsed
		if time_left < _FADE_SECS:
			alpha = clampf(time_left / _FADE_SECS, 0.0, 1.0)
	draw_circle(Vector2.ZERO, size, Color(_color, alpha))

# Spring-overshoot growth curve: 0 → 1.4 (peak) → 1.0
func _splat_size(t: float) -> float:
	if t <= 0.0: return 0.0
	if t >= 1.0: return 1.0
	var a := 0.6
	var b := 0.4
	var grow   := (1.0 + b) * t / a
	var shrink := -(((1.0 + b) * t) - ((2.0 + b) * a)) / a
	return clampf(minf(grow, shrink), 0.0, 1.0 + b)
