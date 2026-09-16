# Copyright 2026 bitHeads, Inc. All Rights Reserved.
class_name LobbyStateColors
extends RefCounted

## Maps a lobby's `state` string (from GET_LOBBY_INSTANCES/GET_LOBBY_DATA) to a status
## colour, shared by LobbyListPanel (row icon) and LobbyDetailPanel (state dot). Not an
## exhaustive/confirmed list of every possible state — unrecognized values fall back to
## grey rather than guessing, since only "early" and "active" have been observed live.

const COLORS := {
	"active": Color(0.25, 0.8, 0.3),      # match is running
	"onTime": Color(0.25, 0.8, 0.3),
	"starting": Color(0.95, 0.65, 0.1),   # about to transition to active
	"early": Color(0.9, 0.8, 0.15),       # forming, waiting on players
	"tooLate": Color(0.9, 0.5, 0.2),
	"dropDead": Color(0.85, 0.25, 0.25),
	"disbanded": Color(0.85, 0.25, 0.25),
}
const DEFAULT_COLOR := Color(0.6, 0.6, 0.6) # unrecognized state

static func color_for(state: String) -> Color:
	return COLORS.get(state, DEFAULT_COLOR)

var _dot_cache: Dictionary = {}

## Small filled-circle texture for the given state — cached per colour so repeated rows/
## refreshes don't regenerate the same image.
func dot_texture_for(state: String, diameter: int = 10) -> ImageTexture:
	var color := color_for(state)
	var key := "%s_%d" % [color.to_html(false), diameter]
	if _dot_cache.has(key):
		return _dot_cache[key]

	var image := Image.create(diameter, diameter, false, Image.FORMAT_RGBA8)
	var radius := diameter / 2.0
	var center := Vector2(radius, radius)
	for y in range(diameter):
		for x in range(diameter):
			if Vector2(x + 0.5, y + 0.5).distance_to(center) <= radius:
				image.set_pixel(x, y, color)
	var texture := ImageTexture.create_from_image(image)
	_dot_cache[key] = texture
	return texture
