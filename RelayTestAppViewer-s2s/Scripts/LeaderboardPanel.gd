# Copyright 2026 bitHeads, Inc. All Rights Reserved.
class_name LeaderboardPanel
extends VBoxContainer

## Reads a CursorParty leaderboard via S2S — a first-class, fully-existing brainCloud
## service, no gaps to work around here. Read-only, polled. Offers all of CursorParty's
## known leaderboards (Ids.LEADERBOARD_IDS — there's no "AllLeaderboards" global property
## to discover them dynamically, so these are reused directly from the real client, see
## cpp-examples/relaytestapp/src/globals.h).

const REFRESH_INTERVAL_SECS := 15.0
const PAGE_SIZE := 20

var _context: S2SContext = null
var _leaderboard_id: String = ""
var _item_list: ItemList
var _refresh_timer: Timer

func _ready() -> void:
	var title := Label.new()
	title.text = "Leaderboard"
	add_child(title)

	var picker := OptionButton.new()
	for id in Ids.LEADERBOARD_IDS:
		picker.add_item(id)
	picker.item_selected.connect(func(index: int): _leaderboard_id = Ids.LEADERBOARD_IDS[index]; refresh())
	add_child(picker)
	_leaderboard_id = Ids.LEADERBOARD_IDS[0] if Ids.LEADERBOARD_IDS.size() > 0 else ""

	_item_list = ItemList.new()
	_item_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	add_child(_item_list)

	_refresh_timer = Timer.new()
	_refresh_timer.wait_time = REFRESH_INTERVAL_SECS
	_refresh_timer.autostart = false
	_refresh_timer.timeout.connect(func(): refresh())
	add_child(_refresh_timer)

func set_context(context: S2SContext) -> void:
	_context = context
	refresh()
	_refresh_timer.start()

func refresh() -> void:
	if _context == null or _leaderboard_id.is_empty():
		return

	var response := await _context.request({
		"service": "leaderboard",
		"operation": "GET_GLOBAL_LEADERBOARD_PAGE",
		"data": {
			"leaderboardId": _leaderboard_id,
			"sort": "HIGH_TO_LOW",
			"startIndex": 0,
			"endIndex": PAGE_SIZE,
		},
	})

	_item_list.clear()

	if int(response.get("status", -1)) != 200:
		_item_list.add_item("(leaderboard unavailable)")
		return

	var entries: Array = response.get("data", {}).get("leaderboard", [])
	if entries.is_empty():
		_item_list.add_item("(no scores yet)")
		return

	for entry in entries:
		if typeof(entry) != TYPE_DICTIONARY:
			continue
		var rank := int(entry.get("rank", 0))
		var player_name := String(entry.get("name", entry.get("playerName", "?")))
		var score = entry.get("score", 0)
		_item_list.add_item("#%d  %s — %s" % [rank, player_name, str(score)])
