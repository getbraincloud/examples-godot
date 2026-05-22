# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Persistent paint mark. Spring-overshoot pop, organic satellite droplets, fades at end of life.
extends Node2D

const _SPLAT_ANIM_TIME  := 0.3
const _BASE_RADIUS      := 16.0
const _BASE_ALPHA       := 0.55   # matches C++ RelayTestApp
const _FADE_SECS        := 3.0
const _SATELLITE_COUNT  := 5

var _color:      Color         = Color.WHITE
var _elapsed:    float         = 0.0
var _duration:   int           = -1
var _satellites: Array[Vector2] = []
var _sat_radii:  Array[float]   = []

func setup(color: Color, duration: int) -> void:
	_color = Color(
		clampf(color.r + randf_range(-0.07, 0.07), 0.0, 1.0),
		clampf(color.g + randf_range(-0.07, 0.07), 0.0, 1.0),
		clampf(color.b + randf_range(-0.07, 0.07), 0.0, 1.0)
	)
	_duration = duration
	for i in range(_SATELLITE_COUNT):
		var angle := randf() * TAU
		var dist  := randf_range(14.0, 26.0)
		_satellites.append(Vector2(cos(angle), sin(angle)) * dist)
		_sat_radii.append(randf_range(3.0, 6.0))

func _process(delta: float) -> void:
	_elapsed += delta
	if _duration > 0 and _elapsed >= float(_duration):
		queue_free()
		return
	queue_redraw()

func _draw() -> void:
	var t    := clampf(_elapsed / _SPLAT_ANIM_TIME, 0.0, 1.0)
	var size := _splat_size(t) * _BASE_RADIUS
	var alpha := _BASE_ALPHA
	if _duration > 0:
		var time_left := float(_duration) - _elapsed
		if time_left < _FADE_SECS:
			alpha = clampf(time_left / _FADE_SECS, 0.0, 1.0) * _BASE_ALPHA

	draw_circle(Vector2.ZERO, size, Color(_color, alpha))

	# Satellites appear slightly after the main blob
	var sat_t := clampf((_elapsed - 0.05) / _SPLAT_ANIM_TIME, 0.0, 1.0)
	for i in range(_satellites.size()):
		draw_circle(_satellites[i], _sat_radii[i] * sat_t, Color(_color, alpha * 0.75))

# Spring-overshoot: 0 → ~1.2 peak → 1.0 settle
func _splat_size(t: float) -> float:
	if t <= 0.0: return 0.0
	if t >= 1.0: return 1.0
	var a    := 0.6
	var b    := 0.4
	var grow   := (1.0 + b) * t / a
	var shrink := -(((1.0 + b) * t) - ((2.0 + b) * a)) / a
	return clampf(minf(grow, shrink), 0.0, 1.0 + b)
