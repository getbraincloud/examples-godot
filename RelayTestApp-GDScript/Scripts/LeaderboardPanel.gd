# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Shared leaderboard viewer — top 5 + "you" row, toggleable between the two boards this app
# posts to (points / coverage) and Lifetime vs Quarterly. Mirrors leaderboardPanel.cpp /
# LeaderboardPanel.js / LeaderboardPanel.cs.
#
# Scores only show up once a match's coverage/win-ranking is computed and the host calls
# PostMatchResults — until a match finishes, this just shows "No scores yet."
class_name LeaderboardPanel
extends VBoxContainer

# Reads the same AppState.*_leaderboard_id(_quarterly) fields LoginScreen resolves from the
# PointsLeaderboardId/CoverageLeaderboardId global-property overrides (falling back to the
# same CursorParty_* defaults) — so this viewer always matches whatever board
# _host_post_match_results_to_cloud actually posted to (mirrors cpp reading
# state.*LeaderboardId directly, and js/C# sharing their resolved ids).
func _leaderboard_id(board_type: String, period: String) -> String:
	if board_type == "coverage":
		return AppState.coverage_leaderboard_id_quarterly if period == "quarterly" else AppState.coverage_leaderboard_id
	return AppState.points_leaderboard_id_quarterly if period == "quarterly" else AppState.points_leaderboard_id

@onready var _points_btn:   Button        = %PointsButton
@onready var _coverage_btn: Button        = %CoverageButton
@onready var _lifetime_btn: Button        = %LifetimeButton
@onready var _quarterly_btn: Button       = %QuarterlyButton
@onready var _rows:         VBoxContainer = %Rows
@onready var _empty_label:  Label         = %EmptyLabel

var _board_type: String = "points"    # "points" | "coverage"
var _period: String = "lifetime"      # "lifetime" | "quarterly"
var _results_by_key: Dictionary = {}  # "<boardType>:<period>" -> {top: [...], self: {...}|null}
var _pending_keys: Dictionary = {}

func _ready() -> void:
	_points_btn.pressed.connect(func(): _set_board_type("points"))
	_coverage_btn.pressed.connect(func(): _set_board_type("coverage"))
	_lifetime_btn.pressed.connect(func(): _set_period("lifetime"))
	_quarterly_btn.pressed.connect(func(): _set_period("quarterly"))
	_refresh_toggle_buttons()
	_fetch_if_needed()
	_render()

# Gold/silver/bronze/white — shared with the match summary screen's rank display.
static func rank_color(rank: int) -> Color:
	if rank == 1: return Color.html("ffd700")
	if rank == 2: return Color.html("c0c0c0")
	if rank == 3: return Color.html("cc8833")
	return Color.WHITE

static func _format_score(score: int, board_type: String) -> String:
	if board_type == "coverage":
		return "%.1f%%" % (score / 100.0)
	return String.num_int64(score)

func _current_key() -> String:
	return "%s:%s" % [_board_type, _period]

func _set_board_type(board_type: String) -> void:
	_board_type = board_type
	_refresh_toggle_buttons()
	_fetch_if_needed()
	_render()

func _set_period(period: String) -> void:
	_period = period
	_refresh_toggle_buttons()
	_fetch_if_needed()
	_render()

func _refresh_toggle_buttons() -> void:
	_points_btn.modulate = Color(1.4, 1.4, 1.4) if _board_type == "points" else Color.WHITE
	_coverage_btn.modulate = Color(1.4, 1.4, 1.4) if _board_type == "coverage" else Color.WHITE
	_lifetime_btn.modulate = Color(1.4, 1.4, 1.4) if _period == "lifetime" else Color.WHITE
	_quarterly_btn.modulate = Color(1.4, 1.4, 1.4) if _period == "quarterly" else Color.WHITE

func _parse_entry(entry: Dictionary) -> Dictionary:
	return {
		"name": entry.get("data", {}).get("name", "Player"),
		"score": int(entry.get("score", 0)),
		"rank": int(entry.get("rank", 0)),
	}

func _fetch_if_needed() -> void:
	if AppState.bc == null:
		return
	var key := _current_key()
	if _results_by_key.has(key) or _pending_keys.has(key):
		return
	_pending_keys[key] = true

	var leaderboard_id: String = _leaderboard_id(_board_type, _period)

	var page_result: Dictionary = await AppState.bc.social_leaderboard_service.get_global_leaderboard_page(
		leaderboard_id, "HIGH_TO_LOW", 0, 4)
	var top: Array = []
	if page_result.get("status", 0) == 200:
		for e in page_result.get("data", {}).get("leaderboard", []):
			top.append(_parse_entry(e))
	_merge_result(key, {"top": top})

	# before_count=0/after_count=0 returns just the current player's own entry.
	var view_result: Dictionary = await AppState.bc.social_leaderboard_service.get_global_leaderboard_view(
		leaderboard_id, "HIGH_TO_LOW", 0, 0)
	var self_entry = null
	if view_result.get("status", 0) == 200:
		var entries: Array = view_result.get("data", {}).get("leaderboard", [])
		if entries.size() > 0:
			self_entry = _parse_entry(entries[0])
	_merge_result(key, {"self": self_entry})

	_pending_keys.erase(key)
	if key == _current_key():
		_render()

func _merge_result(key: String, partial: Dictionary) -> void:
	var current: Dictionary = _results_by_key.get(key, {"top": [], "self": null})
	for k in partial:
		current[k] = partial[k]
	_results_by_key[key] = current

func _render() -> void:
	for child in _rows.get_children():
		child.queue_free()

	var results: Dictionary = _results_by_key.get(_current_key(), {"top": [], "self": null})
	var top: Array = results.get("top", [])
	var self_entry = results.get("self", null)
	var already_shown := self_entry != null and top.any(func(row): return row["rank"] == self_entry["rank"])

	_empty_label.visible = top.is_empty()

	for row in top:
		_rows.add_child(_build_row(row["rank"], row["name"], row["score"], false))

	if self_entry != null and not already_shown:
		if top.size() > 0:
			var ellipsis := Label.new()
			ellipsis.text = "..."
			ellipsis.add_theme_font_size_override("font_size", 11)
			ellipsis.add_theme_color_override("font_color", Color(0.6, 0.6, 0.6))
			_rows.add_child(ellipsis)
		_rows.add_child(_build_row(self_entry["rank"], self_entry["name"] + " (You)", self_entry["score"], true))

func _build_row(rank: int, name: String, score: int, is_me: bool) -> HBoxContainer:
	var row := HBoxContainer.new()
	row.add_theme_constant_override("separation", 6)

	var rank_lbl := Label.new()
	rank_lbl.text = "#%d" % rank
	rank_lbl.custom_minimum_size = Vector2(32, 0)
	rank_lbl.add_theme_color_override("font_color", rank_color(rank))

	var name_lbl := Label.new()
	name_lbl.text = name
	name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	if is_me:
		name_lbl.add_theme_color_override("font_color", Color(0.35, 1.0, 0.45))

	var score_lbl := Label.new()
	score_lbl.text = _format_score(score, _board_type)
	if is_me:
		score_lbl.add_theme_color_override("font_color", Color(0.35, 1.0, 0.45))

	row.add_child(rank_lbl)
	row.add_child(name_lbl)
	row.add_child(score_lbl)
	return row
