# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Persistent paint mark. Spring-overshoot pop, organic satellite droplets, fades at end of life.
extends Node2D

const _SPLAT_ANIM_TIME  := 0.3
const _BASE_RADIUS      := 16.0
const _BASE_ALPHA       := 0.55
const _FADE_SECS        := 3.0
const _SATELLITE_COUNT  := 5

# LCG constants — must match splotchInit() in globals.h exactly.
const _LCG_MUL      := 1664525
const _LCG_INC      := 1013904223
const _LCG_MASK     := 0xFFFFFFFF    # lower 32 bits
const _LCG_NORM     := 4294967296.0  # 2^32 — matches 4294967295.0f compiled as float32 in C++

# Splotch visual parameters — must match globals.h.
const _COLOR_JITTER := 0.07
const _SAT_DIST_MIN := 14.0
const _SAT_DIST_MAX := 26.0
const _SAT_RAD_MIN  := 3.0
const _SAT_RAD_MAX  := 6.0

var _color:      Color          = Color.WHITE
var _elapsed:    float          = 0.0
var _duration:   int            = -1
var _satellites: Array[Vector2] = []
var _sat_radii:  Array[float]   = []

var _lcg_state: int = 0

func _lcg() -> int:
	_lcg_state = (_lcg_state * _LCG_MUL + _LCG_INC) & _LCG_MASK
	return _lcg_state

func _lcg_float(lo: float, hi: float) -> float:
	return lo + float(_lcg()) / _LCG_NORM * (hi - lo)

func setup(color: Color, duration: int, seed: int = -1) -> void:
	_lcg_state = seed if seed >= 0 else randi()
	_color = Color(
		clampf(color.r + _lcg_float(-_COLOR_JITTER, _COLOR_JITTER), 0.0, 1.0),
		clampf(color.g + _lcg_float(-_COLOR_JITTER, _COLOR_JITTER), 0.0, 1.0),
		clampf(color.b + _lcg_float(-_COLOR_JITTER, _COLOR_JITTER), 0.0, 1.0)
	)
	_duration = duration
	for i in range(_SATELLITE_COUNT):
		var angle := _lcg_float(0.0, TAU)
		var dist  := _lcg_float(_SAT_DIST_MIN, _SAT_DIST_MAX)
		_satellites.append(Vector2(cos(angle), sin(angle)) * dist)
		_sat_radii.append(_lcg_float(_SAT_RAD_MIN, _SAT_RAD_MAX))

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
