# Copyright 2026 bitHeads, Inc. All Rights Reserved.
class_name GlobalProperties
extends RefCounted

## Small S2S helper for reading GlobalApp properties (AllLobbyTypes, Colours, ...) — see
## CLAUDE.md's "Global-property-driven config" convention. Always tolerant of a missing/
## unparseable property (returns "" / an empty Array), matching the same rule every RTA
## client follows: never index these dicts blindly.

## Fetches one property by name and returns its raw value as a String, or "" if missing.
## Confirmed response shape against the live internal server (app 23649):
##   {"data": {"<name>": {"name": "<name>", "value": "<the actual value, itself often JSON or CSV>"}}, "status": 200}
static func fetch_raw(context: S2SContext, property_name: String) -> String:
	var response := await context.request({
		"service": "globalApp",
		"operation": "READ_SELECTED_PROPERTIES",
		"data": {"propertyNames": [property_name]},
	})
	if int(response.get("status", -1)) != 200:
		return ""

	var raw = response.get("data", {}).get(property_name, {}).get("value", "")
	if typeof(raw) != TYPE_STRING:
		return ""
	return raw

## Fetches the "Colours" property — confirmed on the live server to be a comma-separated
## list of hex codes with no leading "#" (e.g. "FF3333,FF8800,...") — and returns it as an
## Array of Color. Empty Array on failure — callers must rebuild swatches to the returned
## size, never assume a fixed palette length (the original Godot client bug this convention
## guards against).
static func fetch_colors(context: S2SContext) -> Array:
	var raw := await fetch_raw(context, "Colours")
	if raw.is_empty():
		return []

	var colors: Array = []
	for hex in raw.split(","):
		var trimmed := hex.strip_edges()
		if not trimmed.is_empty():
			colors.append(Color.html(trimmed))
	return colors
