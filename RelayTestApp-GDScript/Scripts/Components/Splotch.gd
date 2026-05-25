# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Persistent paint splatter mark. Spring-overshoot pop, synced rotation, fades at end of life.
extends Node2D

const _SPLAT_ANIM_TIME := 0.3
const _BASE_ALPHA      := 0.55
const _FADE_SECS       := 3.0
const _DISPLAY_SIZE    := 64.0   # game-world diameter — matches SPLOTCH_DISPLAY_SIZE in globals.h

@onready var _sprite: Sprite2D = $Sprite2D

var _color:    Color = Color.WHITE
var _elapsed:  float = 0.0
var _duration: int   = -1

func setup(color: Color, duration: int, angle: float) -> void:
	_color         = color
	_duration      = duration
	_sprite.rotation = angle
	_sprite.modulate  = Color(color, _BASE_ALPHA)
	var tex_w := float(_sprite.texture.get_width())
	_sprite.scale = Vector2.ONE * (_DISPLAY_SIZE / tex_w)

func _process(delta: float) -> void:
	_elapsed += delta
	if _duration > 0 and _elapsed >= float(_duration):
		queue_free()
		return

	var t          := clampf(_elapsed / _SPLAT_ANIM_TIME, 0.0, 1.0)
	var size_scale := _splat_size(t)

	var alpha := _BASE_ALPHA
	if _duration > 0:
		var time_left := float(_duration) - _elapsed
		if time_left < _FADE_SECS:
			alpha = clampf(time_left / _FADE_SECS, 0.0, 1.0) * _BASE_ALPHA

	_sprite.modulate = Color(_color, alpha)
	var tex_w := float(_sprite.texture.get_width())
	_sprite.scale = Vector2.ONE * (_DISPLAY_SIZE / tex_w) * size_scale

# Spring-overshoot: 0 → ~1.4 peak → 1.0 settle (matches Unity AnimateSplatter.SplatSizeOverTime)
func _splat_size(t: float) -> float:
	if t <= 0.0: return 0.0
	if t >= 1.0: return 1.0
	const a := 0.6
	const b := 0.4
	return clampf(minf((1.0 + b) * t / a, -(((1.0 + b) * t) - ((2.0 + b) * a)) / a), 0.0, 1.0 + b)
