# Copyright 2026 bitHeads, Inc. All Rights Reserved.
class_name LobbyListPanel
extends VBoxContainer

## Polls GET_LOBBY_INSTANCES (S2S) for every known CursorParty lobby type and renders the
## live list, each row showing a status dot (state) and how long that lobby has been
## active. Rows are updated in place on refresh — existing rows are never cleared and
## rebuilt, only added/updated/removed as lobbies appear/change/disappear, so the list
## doesn't visibly flash/reset every poll. Read-only — this panel never joins, creates,
## or modifies a lobby.

## GET_LOBBY_INSTANCES takes one lobbyType per call, and AllLobbyTypes on the real
## RelayTestApp app (23649) lists ~28 types (regional/hosting variants of a handful of
## real game modes: CursorPartyV2, CursorPartyV2Backfill, TeamCursorPartyV2, ...) —
## querying every one sequentially each refresh (S2SContext.request() only allows one
## request in flight at a time) takes real time, so this stays a slow poll rather than a
## snappy one.
const REFRESH_INTERVAL_SECS := 20.0
const DURATION_TICK_SECS := 1.0
# Fallback if the AllLobbyTypes global property can't be read/parsed — see _fetch_lobby_types().
const DEFAULT_LOBBY_TYPES := ["CursorPartyV2"]

signal lobby_selected(lobby_id: String)

var _context: S2SContext = null
var _lobby_types: Array = []
var _state_colors := LobbyStateColors.new()
# lobby_id -> { text_base: String, state: String, created_at_secs: float (-1.0 = not yet known) }
var _rows: Dictionary = {}

var _title: Label
var _status_label: Label
var _item_list: ItemList
var _refresh_timer: Timer
var _duration_timer: Timer

func _ready() -> void:
	_title = Label.new()
	_title.text = "Live Lobbies"
	add_child(_title)

	var toolbar := HBoxContainer.new()
	add_child(toolbar)

	var refresh_button := Button.new()
	refresh_button.text = "Refresh"
	refresh_button.pressed.connect(func(): refresh())
	toolbar.add_child(refresh_button)

	_status_label = Label.new()
	_status_label.text = ""
	toolbar.add_child(_status_label)

	_item_list = ItemList.new()
	_item_list.size_flags_vertical = Control.SIZE_EXPAND_FILL
	_item_list.item_selected.connect(_on_item_selected)
	add_child(_item_list)

	_refresh_timer = Timer.new()
	_refresh_timer.wait_time = REFRESH_INTERVAL_SECS
	_refresh_timer.autostart = false
	_refresh_timer.timeout.connect(func(): refresh())
	add_child(_refresh_timer)

	_duration_timer = Timer.new()
	_duration_timer.wait_time = DURATION_TICK_SECS
	_duration_timer.autostart = false
	_duration_timer.timeout.connect(_tick_durations)
	add_child(_duration_timer)

func set_context(context: S2SContext) -> void:
	_context = context
	_fetch_lobby_types.call_deferred()

func _fetch_lobby_types() -> void:
	_lobby_types = DEFAULT_LOBBY_TYPES.duplicate()

	var parsed_types := await _fetch_and_parse_lobby_types()
	if parsed_types.size() > 0:
		_lobby_types = parsed_types
	else:
		push_warning("LobbyListPanel: could not read/parse AllLobbyTypes, using default lobby types: %s" % str(DEFAULT_LOBBY_TYPES))

	refresh()
	_refresh_timer.start()
	_duration_timer.start()

## AllLobbyTypes is a JSON object { "<key>": { "lobby": "TypeName" }, ... } per CLAUDE.md's
## "Global-property-driven config" convention.
func _fetch_and_parse_lobby_types() -> Array:
	var raw := await GlobalProperties.fetch_raw(_context, "AllLobbyTypes")
	if raw.is_empty():
		return []

	var json := JSON.new()
	if json.parse(raw) != OK or typeof(json.get_data()) != TYPE_DICTIONARY:
		return []

	var types: Array = []
	for entry in json.get_data().values():
		if typeof(entry) == TYPE_DICTIONARY and entry.has("lobby"):
			var lobby_type := String(entry["lobby"])
			if not types.has(lobby_type):
				types.append(lobby_type)
	return types

## Adds/updates rows for every lobby found this pass, then removes rows for lobbies that
## disappeared (ended/expired) — the list is never cleared wholesale, so an already-visible
## row's selection/scroll position survives a refresh.
func refresh() -> void:
	if _context == null or _lobby_types.is_empty():
		return

	_status_label.text = "Loading..."
	var seen_lobby_ids: Dictionary = {}

	for lobby_type in _lobby_types:
		# criteriaJson.rating is a required server-side filter, not optional — a huge
		# range effectively means "any rating" (confirmed against the live server: an
		# empty criteriaJson is rejected with "Required criteriaJson parameter 'rating' is missing").
		var response := await _context.request({"service": "lobby", "operation": "GET_LOBBY_INSTANCES", "data": {"lobbyType": lobby_type, "criteriaJson": {"rating": {"min": 0, "max": 999999}}}})
		if int(response.get("status", -1)) != 200:
			continue

		var lobbies_by_rating: Dictionary = response.get("data", {}).get("lobbiesByRating", {})
		for rating_bucket in lobbies_by_rating.values():
			if typeof(rating_bucket) != TYPE_ARRAY:
				continue
			for summary in rating_bucket:
				var lobby_id := String(summary.get("id", ""))
				if lobby_id.is_empty():
					continue
				seen_lobby_ids[lobby_id] = true
				_upsert_lobby_item(lobby_type, summary)

	_remove_stale_rows(seen_lobby_ids)
	_status_label.text = "%d live" % _item_list.item_count if _item_list.item_count > 0 else "No live lobbies"

func _find_row_index(lobby_id: String) -> int:
	for idx in range(_item_list.item_count):
		if String(_item_list.get_item_metadata(idx)) == lobby_id:
			return idx
	return -1

func _upsert_lobby_item(lobby_type: String, summary: Dictionary) -> void:
	var lobby_id := String(summary.get("id", ""))
	var state := String(summary.get("state", "?"))
	var num_members := int(summary.get("numMembers", 0))
	var max_members := int(summary.get("maxMembers", 0))
	var owner_name := String(summary.get("owner", {}).get("name", "?"))
	var text_base := "[%s] %d/%d — %s" % [lobby_type, num_members, max_members, owner_name]

	var idx := _find_row_index(lobby_id)
	var is_new := idx < 0
	if is_new:
		idx = _item_list.add_item("")
		_item_list.set_item_metadata(idx, lobby_id)

	_item_list.set_item_icon(idx, _state_colors.dot_texture_for(state))

	if not _rows.has(lobby_id):
		_rows[lobby_id] = {"text_base": text_base, "state": state, "created_at_secs": -1.0}
		_fetch_created_at(lobby_id)
	else:
		_rows[lobby_id]["text_base"] = text_base
		_rows[lobby_id]["state"] = state

	_update_row_text(lobby_id)

func _remove_stale_rows(seen_lobby_ids: Dictionary) -> void:
	var indices_to_remove: Array = []
	for lobby_id in _rows.keys():
		if seen_lobby_ids.has(lobby_id):
			continue
		var idx := _find_row_index(lobby_id)
		if idx >= 0:
			indices_to_remove.append(idx)
		_rows.erase(lobby_id)

	# Remove highest index first so earlier removals don't shift indices still pending removal.
	indices_to_remove.sort()
	indices_to_remove.reverse()
	for idx in indices_to_remove:
		_item_list.remove_item(idx)

## GET_LOBBY_INSTANCES' summary has no timestamp — only the fuller GET_LOBBY_DATA carries
## timetable.createdAt (confirmed against the live server, ms since epoch). One extra
## round-trip per *found* lobby (not per type — most of the 28 types come back empty), so
## this stays cheap in practice. Fire-and-forget: doesn't block the rest of the list from
## rendering.
func _fetch_created_at(lobby_id: String) -> void:
	var response := await _context.request({"service": "lobby", "operation": "GET_LOBBY_DATA", "data": {"lobbyId": lobby_id}})
	if not _rows.has(lobby_id):
		return # row was removed (lobby ended) while this call was in flight
	if int(response.get("status", -1)) != 200:
		return

	var created_at_ms = response.get("data", {}).get("timetable", {}).get("createdAt", null)
	if typeof(created_at_ms) != TYPE_FLOAT and typeof(created_at_ms) != TYPE_INT:
		return

	_rows[lobby_id]["created_at_secs"] = float(created_at_ms) / 1000.0
	_update_row_text(lobby_id)

func _tick_durations() -> void:
	for lobby_id in _rows.keys():
		if _rows[lobby_id]["created_at_secs"] > 0.0:
			_update_row_text(lobby_id)

func _update_row_text(lobby_id: String) -> void:
	var idx := _find_row_index(lobby_id)
	if idx < 0:
		return
	var row: Dictionary = _rows[lobby_id]

	var duration_text := "..."
	if row["created_at_secs"] > 0.0:
		var elapsed_secs := int(Time.get_unix_time_from_system() - row["created_at_secs"])
		duration_text = _format_duration(max(elapsed_secs, 0))

	_item_list.set_item_text(idx, "%s — %s" % [row["text_base"], duration_text])

func _format_duration(total_secs: int) -> String:
	var hours := total_secs / 3600
	var minutes := (total_secs % 3600) / 60
	var seconds := total_secs % 60
	if hours > 0:
		return "%d:%02d:%02d" % [hours, minutes, seconds]
	return "%02d:%02d" % [minutes, seconds]

func _on_item_selected(index: int) -> void:
	var lobby_id := String(_item_list.get_item_metadata(index))
	if not lobby_id.is_empty():
		lobby_selected.emit(lobby_id)
