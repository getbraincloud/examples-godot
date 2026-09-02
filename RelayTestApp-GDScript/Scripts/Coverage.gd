# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Canvas coverage % + win-ranking (BCLOUD-14472/14490) — pure port of cpp's coverage.cpp /
# the react/C# ports' coverage.js / Coverage.cs. See Coverage.cs for the full algorithm
# writeup; this must stay numerically identical (same constants, same paint-order-wins
# rasterization) so scores match across clients sharing a lobby.
class_name Coverage
extends RefCounted

const CANVAS_W := 800.0
const CANVAS_H := 600.0
const SPLOTCH_RADIUS := 32.0 # SPLOTCH_DISPLAY_SIZE (64) * 0.5
const GRID_CELL_SIZE := 2.0

const GRID_W := 400 # int(CANVAS_W / GRID_CELL_SIZE)
const GRID_H := 300 # int(CANVAS_H / GRID_CELL_SIZE)

static var _owner_grid: PackedInt32Array = PackedInt32Array()

# splotches: Array of {x, y, c} (normalized 0-1 x/y, c = color index — GameScreen's
# _splotch_records format). members: Array of {cx_id, color_index}.
# Returns an Array of {cx_id, color_index, coverage_pct, rank, beaten}, sorted by rank.
static func compute(splotches: Array, members: Array) -> Array:
	var result: Array = []
	for m in members:
		result.append({
			"cx_id": m.get("cx_id", ""),
			"color_index": int(m.get("color_index", 0)),
			"visible_count": 0,
			"coverage_pct": 0.0,
			"rank": 1,
			"beaten": 0,
		})

	var index_by_cx_id: Dictionary = {}
	for k in result.size():
		index_by_cx_id[result[k]["cx_id"]] = k

	var n := splotches.size()
	if n > 0:
		if _owner_grid.size() != GRID_W * GRID_H:
			_owner_grid.resize(GRID_W * GRID_H)
		_owner_grid.fill(-1)

		var cell_radius := int(ceil(SPLOTCH_RADIUS / GRID_CELL_SIZE))
		var r2 := SPLOTCH_RADIUS * SPLOTCH_RADIUS

		for s: Dictionary in splotches:
			var px: float = float(s.get("x", 0.0)) * CANVAS_W
			var py: float = float(s.get("y", 0.0)) * CANVAS_H
			var color_index: int = int(s.get("c", 0))

			# Attribute by the sender's actual cxId first — required so two players who
			# picked the same colour don't get merged into a single coverage total. Falls
			# back to a colorIndex match only for unattributed splotches (a JIP sync entry
			# whose owning netId couldn't be resolved, or a legacy sender).
			var sender_cx: String = s.get("cx", "")
			var idx := -1
			if sender_cx != "" and index_by_cx_id.has(sender_cx):
				idx = index_by_cx_id[sender_cx]
			else:
				for k in result.size():
					if result[k]["color_index"] == color_index:
						idx = k
						break

			var grid_cx := int(px / GRID_CELL_SIZE)
			var grid_cy := int(py / GRID_CELL_SIZE)
			for dy in range(-cell_radius, cell_radius + 1):
				var gy := grid_cy + dy
				if gy < 0 or gy >= GRID_H:
					continue
				var cell_py := (gy + 0.5) * GRID_CELL_SIZE
				var ddy := cell_py - py
				var row_base := gy * GRID_W
				for dx in range(-cell_radius, cell_radius + 1):
					var gx := grid_cx + dx
					if gx < 0 or gx >= GRID_W:
						continue
					var cell_px := (gx + 0.5) * GRID_CELL_SIZE
					var ddx := cell_px - px
					if ddx * ddx + ddy * ddy <= r2:
						_owner_grid[row_base + gx] = idx # may be -1 — still overwrites, credits no one

		for c in _owner_grid.size():
			var idx: int = _owner_grid[c]
			if idx >= 0:
				result[idx]["visible_count"] += 1

	var cell_area := GRID_CELL_SIZE * GRID_CELL_SIZE
	var canvas_area := CANVAS_W * CANVAS_H
	for e in result:
		e["coverage_pct"] = minf(100.0, e["visible_count"] * cell_area / canvas_area * 100.0)

	result.sort_custom(func(a, b):
		if a["coverage_pct"] != b["coverage_pct"]:
			return a["coverage_pct"] > b["coverage_pct"]
		if a["visible_count"] != b["visible_count"]:
			return a["visible_count"] > b["visible_count"]
		return a["cx_id"] < b["cx_id"]
	)

	for i in result.size():
		if i > 0 and result[i]["coverage_pct"] == result[i - 1]["coverage_pct"] \
				and result[i]["visible_count"] == result[i - 1]["visible_count"]:
			result[i]["rank"] = result[i - 1]["rank"] # tie shares rank
		else:
			result[i]["rank"] = i + 1

	for e in result:
		var beaten := 0
		for other in result:
			if other["rank"] > e["rank"]:
				beaten += 1
		e["beaten"] = beaten

	return result
