# Copyright 2026 bitHeads, Inc. All Rights Reserved.
extends Control

signal end_match   # auto-timeout only — the host can no longer end a match prematurely
signal leave_game  # any player Leave Game button
signal host_match_result_ready(round: int, entries: Array) # host only — Main posts to PostMatchResults

const _CURSOR_SCENE    := preload("res://Scenes/Components/PlayerCursor.tscn")
const _SHOCKWAVE_SCENE := preload("res://Scenes/Components/Shockwave.tscn")
const _SPLOTCH_SCENE   := preload("res://Scenes/Components/Splotch.tscn")

const _GAME_W          := 800.0
const _GAME_H          := 600.0
const _MATCH_DURATION  := 90.0
const _COUNTDOWN_START := 0.0
const _MOVE_INTERVAL   := 0.05
const _PING_INTERVAL   := 2.0
const _COVERAGE_RECOMPUTE_INTERVAL := 0.25
const _RESULT_GRACE_SEC            := 3.0
const _CHUNK_MAX_BYTES             := 900

# Hold-to-paint: holding the mouse button down auto-repeats a splotch at this fixed interval
# (the initial click still paints immediately) — same value across cpp/js/both Godot ports so
# holding paints at the same rate for everyone in a shared match.
const _AUTO_PAINT_INTERVAL := 0.15

enum _MatchPhase { RUNNING, RESULTS_BROADCAST, ENDED }

@onready var _game_area:       Control       = %GameArea
@onready var _timer_label:     Label         = %TimerLabel
@onready var _countdown_label: Label         = %CountdownLabel
@onready var _player_panel:    VBoxContainer = %PlayerPanel
@onready var _scoreboard:      VBoxContainer = %Scoreboard
@onready var _move_timer:      Timer         = %MoveTimer
@onready var _leave_btn:       Button        = %LeaveButton
@onready var _my_ping_label:   Label         = %PingLabel

# net_id → PlayerCursor node
var _cursors:      Dictionary = {}
# All active Splotch nodes (for clear_splotches)
var _splotches:    Array      = []
# Serializable splotch canvas {x,y,c,a} mirrored alongside _splotches. The host replays
# this to a join-in-progress member (splotch_sync) so late joiners see splotches that were
# painted before they connected.
var _splotch_records: Array   = []
# cxId → ping Label
var _ping_labels:  Dictionary = {}
# bidirectional cx_id ↔ net_id
var _cx_to_net_id: Dictionary = {}
var _net_id_to_cx: Dictionary = {}
# Shockwave mask: cxId → bool (true = send to this player)
var _send_to:      Dictionary = {}

var _ping_timer: float = 0.0
var _auto_paint_accum: float = 0.0

var _match_phase: int = _MatchPhase.RUNNING
var _coverage_accum: float = 0.0
var _results_sent_at: float = 0.0
var _round_number: int = 0
var _pending_match_result: Array = []

func _ready() -> void:
	# The relay data callback is registered once, centrally, by Main (see _on_relay_data
	# there) and forwarded here — registering it per-screen meant a "lb_result" broadcast
	# that lands right after END_MATCH swaps this screen out for Match Summary would
	# silently go nowhere, since the callback pointed at an already-freed GameScreen.

	_move_timer.wait_time = _MOVE_INTERVAL
	_move_timer.timeout.connect(_send_position)
	_move_timer.start()

	_build_player_panel()
	_countdown_label.hide()
	_game_area.gui_input.connect(_on_game_area_input)

	if AppState.user_cx_id != "" and AppState.my_net_id >= 0:
		_cx_to_net_id[AppState.user_cx_id] = AppState.my_net_id
		_net_id_to_cx[AppState.my_net_id]  = AppState.user_cx_id

	# Fresh round bookkeeping (this screen is recreated once per round by Main).
	AppState.round_number += 1
	_round_number = AppState.round_number
	AppState.match_result_round = -1
	AppState.match_result_entries = []
	AppState.leaderboard_posted_round = -1
	AppState.pending_lb_results.clear()

	_leave_btn.pressed.connect(_on_leave_game_pressed)

# ── Tick ──────────────────────────────────────────────────────────────────────

func _process(delta: float) -> void:
	_ping_timer += delta

	# Render our OWN cursor locally, tracked every frame so the coloured arrow follows the
	# pointer smoothly — CPP/dotnet show the local player their own arrow (remote cursors are
	# driven by relay "move" messages in _on_move, which skips our own net_id).
	if AppState.my_net_id >= 0:
		var lp := _game_area.get_local_mouse_position()
		_get_or_create_cursor(AppState.my_net_id).move_to(
			clampf(lp.x / _GAME_W, 0.0, 1.0), clampf(lp.y / _GAME_H, 0.0, 1.0))

	# Holding the button auto-repeats a paint every _AUTO_PAINT_INTERVAL. Checking the live OS
	# button state (not a pressed/released flag) means it self-corrects even if we miss a
	# release event, e.g. from losing window focus mid-hold.
	if Input.is_mouse_button_pressed(MOUSE_BUTTON_LEFT):
		_auto_paint_accum += delta
		if _auto_paint_accum >= _AUTO_PAINT_INTERVAL:
			_auto_paint_accum = 0.0
			var paint_pos := _game_area.get_local_mouse_position()
			if Rect2(Vector2.ZERO, Vector2(_GAME_W, _GAME_H)).has_point(paint_pos):
				_do_shockwave(paint_pos, AppState.my_color_index, true, -1.0, AppState.user_cx_id)
	else:
		_auto_paint_accum = 0.0

	if AppState.game_start_time_ms <= 0:
		return

	var elapsed   := float(int(Time.get_unix_time_from_system() * 1000.0) - AppState.game_start_time_ms) / 1000.0
	var remaining := maxf(_MATCH_DURATION - elapsed, 0.0)
	_timer_label.text = "%02d:%02d" % [int(remaining) / 60, int(remaining) % 60]

	if elapsed >= _COUNTDOWN_START and elapsed < _MATCH_DURATION:
		_countdown_label.show()
		_countdown_label.text = "%d" % (int(_MATCH_DURATION - elapsed) + 1)
	else:
		_countdown_label.hide()

	if _ping_timer >= _PING_INTERVAL:
		_ping_timer = 0.0
		_broadcast_ping()
		_refresh_own_ping()

	_coverage_accum += delta
	if _coverage_accum >= _COVERAGE_RECOMPUTE_INTERVAL:
		_coverage_accum = 0.0
		_tick_match(elapsed)

# ── Input ─────────────────────────────────────────────────────────────────────

func _on_game_area_input(event: InputEvent) -> void:
	if not (event is InputEventMouseButton):
		return
	var mb := event as InputEventMouseButton
	if mb.pressed and mb.button_index == MOUSE_BUTTON_LEFT:
		var local_pos := _game_area.get_local_mouse_position()
		if Rect2(Vector2.ZERO, Vector2(_GAME_W, _GAME_H)).has_point(local_pos):
			_do_shockwave(local_pos, AppState.my_color_index, true, -1.0, AppState.user_cx_id)

# ── Position broadcast ────────────────────────────────────────────────────────

func _send_position() -> void:
	var local_pos := _game_area.get_local_mouse_position()
	var norm_x    := clampf(local_pos.x / _GAME_W, 0.0, 1.0)
	var norm_y    := clampf(local_pos.y / _GAME_H, 0.0, 1.0)
	_send_relay_all({"op": "move", "data": {"x": norm_x, "y": norm_y}},
					false, true, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1)

# ── Ping broadcast ────────────────────────────────────────────────────────────

func _broadcast_ping() -> void:
	var ms: int = AppState.bc.relay_service.get_ping()
	if ms < 0:
		return
	_send_relay_all({"op": "relay_ping", "data": {"ping": ms}},
					false, false, BrainCloudRelay.CHANNEL_LOW_PRIORITY)

# ── Shockwave + splotch ───────────────────────────────────────────────────────

func _do_shockwave(pos: Vector2, color_index: int, broadcast: bool, angle: float = -1.0, cx_id: String = "") -> void:
	var col := AppState.color_palette[color_index] if color_index < AppState.color_palette.size() else Color.WHITE

	if angle < 0.0:
		angle = randf() * TAU

	var sw := _SHOCKWAVE_SCENE.instantiate() as Node2D
	sw.setup(col)
	sw.position = pos
	_game_area.add_child(sw)

	var sp := _SPLOTCH_SCENE.instantiate() as Node2D
	sp.position = pos
	_game_area.add_child(sp)  # must be before setup so @onready vars are valid
	var now_ms := int(Time.get_unix_time_from_system() * 1000.0)
	sp.setup(col, AppState.splotch_duration, angle)
	_splotches.append(sp)
	# Persist for join-in-progress replay (normalized coords, color index, synced angle, and
	# creation timestamp so a late joiner ages/fades it from the original time — matches CPP).
	_splotch_records.append({"x": pos.x / _GAME_W, "y": pos.y / _GAME_H, "c": color_index, "a": angle, "t": now_ms, "cx": cx_id})

	if broadcast:
		var norm_x := pos.x / _GAME_W
		var norm_y := pos.y / _GAME_H
		var msg    := JSON.stringify({"op": "shockwave", "data": {"x": norm_x, "y": norm_y, "angle": angle}}).to_utf8_buffer()
		_send_to_targets(msg, true, false, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1)

# ── Relay send helpers ────────────────────────────────────────────────────────

# Send to all players unconditionally.
func _send_relay_all(msg: Dictionary, reliable: bool, ordered: bool, channel: int) -> void:
	AppState.bc.relay_service.send(JSON.stringify(msg).to_utf8_buffer(),
								   BrainCloudRelay.TO_ALL_PLAYERS, reliable, ordered, channel)

# Send respecting the player mask checkboxes.
func _send_to_targets(data: PackedByteArray, reliable: bool, ordered: bool, channel: int) -> void:
	if _send_to.is_empty():
		AppState.bc.relay_service.send(data, BrainCloudRelay.TO_ALL_PLAYERS, reliable, ordered, channel)
		return
	var all_on := true
	for v in _send_to.values():
		if not v:
			all_on = false
			break
	if all_on:
		AppState.bc.relay_service.send(data, BrainCloudRelay.TO_ALL_PLAYERS, reliable, ordered, channel)
	else:
		for cx in _send_to:
			if _send_to[cx] and _cx_to_net_id.has(cx):
				AppState.bc.relay_service.send(data, _cx_to_net_id[cx], reliable, ordered, channel)

# ── Relay receive ─────────────────────────────────────────────────────────────

func on_relay_message(net_id: int, raw: PackedByteArray) -> void:
	var msg = JSON.parse_string(raw.get_string_from_utf8())
	if not msg is Dictionary:
		return
	match msg.get("op", ""):
		"game_start":
			var start_time: int = int(msg.get("data", {}).get("startTime", 0))
			if start_time > 0 and AppState.game_start_time_ms == 0:
				AppState.game_start_time_ms = start_time
		"move":            _on_move(net_id, msg.get("data", {}))
		"shockwave":       _on_remote_shockwave(net_id, msg.get("data", {}))
		"relay_ping":      _on_remote_ping(net_id, int(msg.get("data", {}).get("ping", msg.get("ping", -1))))
		"clear_splotches": _clear_all_splotches()
		"splotch_sync":    _on_splotch_sync(msg.get("data", {}))
		"end_match":       end_match.emit()
		"match_result":    _on_match_result_received(msg.get("data", {}))

# ── Coverage / match result (BCLOUD-14472/14489/14490) ───────────────────────

# Live coverage recompute (drives the scoreboard sidebar) + the host's auto-end-at-
# MatchDuration sequence: compute+broadcast match_result, wait ResultGraceSec, then end the
# match the same way the manual "End Match" button does. Ticked every
# CoverageRecomputeInterval while a match is running.
func _tick_match(elapsed: float) -> void:
	var fresh: Array = Coverage.compute(_splotch_records, _coverage_members())
	_update_scoreboard(fresh)

	var is_host := AppState.user_cx_id == AppState.lobby_owner_cx_id

	if _match_phase == _MatchPhase.RUNNING and elapsed >= _MATCH_DURATION and is_host:
		if AppState.match_result_round != _round_number:
			_apply_match_result(_round_number, _to_match_result_entries(fresh))
			_send_match_result(_round_number, fresh)
		_match_phase = _MatchPhase.RESULTS_BROADCAST
		_results_sent_at = elapsed
	elif _match_phase == _MatchPhase.RESULTS_BROADCAST and is_host and elapsed - _results_sent_at >= _RESULT_GRACE_SEC:
		end_match.emit()
		_match_phase = _MatchPhase.ENDED

	# Watchdog: well past the deadline with no authoritative result at all (a dropped
	# broadcast, or a gap during host migration) — compute and apply locally so the round
	# can't hang forever.
	if AppState.match_result_round != _round_number and elapsed >= _MATCH_DURATION + _RESULT_GRACE_SEC + 3.0:
		_apply_match_result(_round_number, _to_match_result_entries(fresh))
		if is_host and _match_phase != _MatchPhase.ENDED:
			_send_match_result(_round_number, fresh)
			_match_phase = _MatchPhase.RESULTS_BROADCAST
			_results_sent_at = elapsed

func _coverage_members() -> Array:
	var list: Array = []
	for m: Dictionary in AppState.lobby_members:
		list.append({"cx_id": m.get("cxId", ""), "color_index": int(m.get("extra", {}).get("colorIndex", 0))})
	return list

func _to_match_result_entries(coverage: Array) -> Array:
	var list: Array = []
	for c: Dictionary in coverage:
		list.append({
			"cx_id": c["cx_id"], "rank": c["rank"], "coverage_pct": c["coverage_pct"],
			"beaten": c["beaten"], "lb_delta": {},
		})
	return list

func _update_scoreboard(coverage: Array) -> void:
	for child in _scoreboard.get_children():
		child.queue_free()
	for c: Dictionary in coverage:
		var member := _member_for_cx(c["cx_id"])
		var name: String = member.get("name", "?")
		var is_me: bool = c["cx_id"] == AppState.user_cx_id

		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 6)

		var swatch := ColorRect.new()
		swatch.custom_minimum_size = Vector2(10, 10)
		swatch.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		if c["color_index"] < AppState.color_palette.size():
			swatch.color = AppState.color_palette[c["color_index"]]

		var rank_lbl := Label.new()
		rank_lbl.text = "#%d" % c["rank"]
		rank_lbl.custom_minimum_size = Vector2(24, 0)
		rank_lbl.add_theme_font_size_override("font_size", 11)
		rank_lbl.add_theme_color_override("font_color", LeaderboardPanel.rank_color(c["rank"]))

		var name_lbl := Label.new()
		name_lbl.text = name + (" (you)" if is_me else "")
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_lbl.add_theme_font_size_override("font_size", 11)
		if is_me:
			name_lbl.add_theme_color_override("font_color", Color(0.35, 1.0, 0.45))

		var pct_lbl := Label.new()
		pct_lbl.text = "%d%%" % int(round(c["coverage_pct"]))
		pct_lbl.add_theme_font_size_override("font_size", 11)
		if is_me:
			pct_lbl.add_theme_color_override("font_color", Color(0.35, 1.0, 0.45))

		row.add_child(swatch)
		row.add_child(rank_lbl)
		row.add_child(name_lbl)
		row.add_child(pct_lbl)
		_scoreboard.add_child(row)

# Broadcasts the host-authoritative match result to the rest of the match, chunked to stay
# under a relay packet's byte budget (mirrors _send_splotch_sync's pattern).
func _send_match_result(round: int, coverage: Array) -> void:
	if coverage.is_empty():
		return
	var entries: Array = []
	for c: Dictionary in coverage:
		entries.append({"cx": c["cx_id"], "r": c["rank"], "c": int(c["coverage_pct"] * 100 + 0.5), "b": c["beaten"]})
	_chunk_and_send("match_result", round, entries, true)

func _on_match_result_received(data: Dictionary) -> void:
	var round: int = int(data.get("round", 0))
	if AppState.match_result_round == round:
		return

	if data.get("first", false):
		_pending_match_result = []
	for e: Dictionary in data.get("e", []):
		_pending_match_result.append({
			"cx_id": e.get("cx", ""), "rank": int(e.get("r", 0)),
			"coverage_pct": float(e.get("c", 0)) / 100.0, "beaten": int(e.get("b", 0)), "lb_delta": {},
		})
	if data.get("last", false):
		_apply_match_result(round, _pending_match_result)
		_pending_match_result = []

# Applies an authoritative coverage snapshot for a round — either locally-computed (host, or
# the watchdog fallback) or reassembled from a "match_result" broadcast. Idempotent per round.
# Only the host posts to the cloud (via Main, so the response survives this screen being
# freed on END_MATCH) — everyone else just waits for the "lb_result" broadcast that produces.
func _apply_match_result(round: int, entries: Array) -> void:
	if AppState.match_result_round == round:
		return
	AppState.match_result_round = round
	AppState.match_result_entries = entries

	# Drain any "lb_result" broadcasts that arrived before this round's match_result did.
	for e: Dictionary in entries:
		var cx: String = e["cx_id"]
		if AppState.pending_lb_results.has(cx):
			e["lb_delta"] = AppState.pending_lb_results[cx]
			AppState.pending_lb_results.erase(cx)

	if AppState.leaderboard_posted_round == round:
		return
	AppState.leaderboard_posted_round = round

	if AppState.user_cx_id == AppState.lobby_owner_cx_id:
		host_match_result_ready.emit(round, entries)

# Called by Main right before it tears down relay and switches away from this screen on
# END_MATCH/DISCONNECT, in case a late join or a dropped/incomplete "match_result" chunk
# means AppState never got populated for this round — same recompute the tick watchdog above
# uses, just triggered immediately instead of waiting out the extra 3s (this screen is about
# to be freed, so the watchdog would never get to run it).
func apply_fallback_if_missing() -> void:
	if AppState.match_result_round != _round_number:
		var fresh: Array = Coverage.compute(_splotch_records, _coverage_members())
		_apply_match_result(_round_number, _to_match_result_entries(fresh))

# "lb_result" is received in Main now (see _on_relay_data / _on_lb_result_received there),
# not here — it needs to keep working after this screen's been torn down for Match Summary.

# Shared chunker for match_result/lb_result — always sends at least one packet so "last"
# always fires, even for an empty entries array (lb_result may legitimately have none).
func _chunk_and_send(op: String, round: int, entries: Array, require_non_empty: bool) -> void:
	if require_non_empty and entries.is_empty():
		return

	var batches: Array = []
	var batch: Array = []
	for entry: Dictionary in entries:
		batch.append(entry)
		var candidate: PackedByteArray = JSON.stringify(
			{"op": op, "data": {"round": round, "first": batches.is_empty(), "last": true, "e": batch}}
		).to_utf8_buffer()
		if candidate.size() > _CHUNK_MAX_BYTES and batch.size() > 1:
			batch.pop_back()
			batches.append(batch)
			batch = [entry]
	batches.append(batch) # always at least one (possibly empty) so "last" still fires

	for i in batches.size():
		var packet: PackedByteArray = JSON.stringify({
			"op": op,
			"data": {"round": round, "first": i == 0, "last": i == batches.size() - 1, "e": batches[i]}
		}).to_utf8_buffer()
		_send_relay_all_bytes(packet, true, true, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1)

func _send_relay_all_bytes(data: PackedByteArray, reliable: bool, ordered: bool, channel: int) -> void:
	AppState.bc.relay_service.send(data, BrainCloudRelay.TO_ALL_PLAYERS, reliable, ordered, channel)

# ── Relay system messages (called by Main.gd) ─────────────────────────────────

func on_relay_system(op: String, msg: Dictionary) -> void:
	match op:
		"CONNECT", "NET_ID":  # NET_ID = existing player's mapping sent on join
			var cx: String = msg.get("cxId", "")
			var nid: int   = msg.get("netId", -1)
			if cx != "" and nid >= 0:
				_cx_to_net_id[cx]  = nid
				_net_id_to_cx[nid] = cx
			# The relay's CONNECT/NET_ID can beat the Lobby service's own MEMBER_JOIN/UPDATE
			# event to us, so AppState.lobby_members may not have this peer yet. Seed a
			# placeholder (blank name/colour) so the coverage board doesn't just omit them —
			# the next lobby event overwrites it with real data. Without this, a JIP joiner's
			# own coverage board can render empty, and a player who disconnects right after
			# connecting can be dropped from the round's results entirely.
			if cx != "":
				var already_known := false
				for m in AppState.lobby_members:
					if m.get("cxId", "") == cx:
						already_known = true
						break
				if not already_known:
					AppState.lobby_members.append({"cxId": cx, "extra": {}})
			# A new player joined mid-match: as host, replay the persisted splotch canvas to
			# them. (Only CONNECT — NET_ID is the existing-player mappings we receive on join.)
			if op == "CONNECT" and nid >= 0 and AppState.user_cx_id == AppState.lobby_owner_cx_id:
				_send_splotch_sync(nid)
		"DISCONNECT":
			var cx: String = msg.get("cxId", "")
			_remove_cursor_for_cx(cx)
		"MIGRATE_OWNER":
			# Relay reassigned the host role after the original host disconnected — update
			# AppState.lobby_owner_cx_id, the same field every isHost check reads. The Lobby
			# service's own owner field catches up too via the next lobby-update event; this
			# relay message is just the faster of the two signals. _tick_match re-evaluates
			# isHost every tick, so the newly-promoted host picks up match duties (auto-end,
			# match_result broadcast) with no extra handoff — it already has _splotch_records
			# and AppState.game_start_time_ms like everyone else. Without this nobody ever
			# recognizes themselves as host again once the original leaves, and the match just
			# hangs.
			var new_owner_cx_id: String = msg.get("cxId", "")
			if not new_owner_cx_id.is_empty():
				AppState.lobby_owner_cx_id = new_owner_cx_id

# ── Relay message handlers ────────────────────────────────────────────────────

func _on_move(net_id: int, data: Dictionary) -> void:
	if net_id == AppState.my_net_id:
		return
	_get_or_create_cursor(net_id).move_to(float(data.get("x", 0.0)), float(data.get("y", 0.0)))

func _on_remote_shockwave(net_id: int, data: Dictionary) -> void:
	if net_id == AppState.my_net_id:
		return  # relay delivers to all including sender; skip own echo
	var cx: String = _net_id_to_cx.get(net_id, "")
	var member: Dictionary = _member_for_cx(cx)
	var color_index: int = member.get("extra", {}).get("colorIndex", 0)
	var angle: float = float(data.get("angle", randf() * TAU))
	var pos := Vector2(float(data.get("x", 0.0)) * _GAME_W,
					   float(data.get("y", 0.0)) * _GAME_H)
	_do_shockwave(pos, color_index, false, angle, cx)

# Format a relay ping for the HUD, matching CPP: "..." until first received, "T/O" at >=999.
func _fmt_ping(ms: int) -> String:
	if ms < 0:    return "..."
	if ms >= 999: return "T/O"
	return "%d ms" % ms

func _on_remote_ping(net_id: int, ms: int) -> void:
	var cx: String = _net_id_to_cx.get(net_id, "")
	if cx != "" and _ping_labels.has(cx):
		(_ping_labels[cx] as Label).text = _fmt_ping(ms)

# ── Clear splotches ───────────────────────────────────────────────────────────

func _clear_all_splotches() -> void:
	for sp in _splotches:
		if is_instance_valid(sp):
			sp.queue_free()
	_splotches.clear()
	_splotch_records.clear()

func _on_splotch_sync(data: Dictionary) -> void:
	if data.get("first", false):
		_clear_all_splotches()
	for entry in data.get("splotches", []):
		var sx   := float(entry.get("x", 0.0)) * _GAME_W
		var sy   := float(entry.get("y", 0.0)) * _GAME_H
		var cidx := int(entry.get("c", 0))
		var ang  := float(entry.get("a", randf() * TAU))
		var t_ms := int(entry.get("t", 0))
		var col  := AppState.color_palette[cidx] if cidx < AppState.color_palette.size() else Color.WHITE
		var sp   := _SPLOTCH_SCENE.instantiate() as Node2D
		sp.position = Vector2(sx, sy)
		_game_area.add_child(sp)
		# Pass the original creation time so age/fade continue from it rather than restarting.
		sp.setup(col, AppState.splotch_duration, ang, t_ms)
		_splotches.append(sp)
		# "o" is a compact netId, resolved to a cxId now — the map that resolves it may not
		# survive past END_MATCH, so this must happen at receive time, never deferred.
		# Missing/unresolvable -> unattributed (coverage falls back to a colorIndex match).
		var owner_cx := ""
		if entry.has("o"):
			owner_cx = _net_id_to_cx.get(int(entry["o"]), "")
		# Keep the serializable canvas in step with what's displayed (carry t forward for re-sync).
		_splotch_records.append({"x": float(entry.get("x", 0.0)), "y": float(entry.get("y", 0.0)), "c": cidx, "a": ang, "t": t_ms, "cx": owner_cx})

# Reshapes a local splotch record ("cx" = full cxId, used for local coverage attribution)
# into its wire form — a compact "o" (netId), not the ~80-char cxId, to stay inside the
# chunk byte budget. Resolved back to a cxId at receive time (see _on_splotch_sync above) —
# never deferred, since the netId map may not survive past END_MATCH.
func _to_wire_splotch(record: Dictionary) -> Dictionary:
	var entry := {"x": record.get("x", 0.0), "y": record.get("y", 0.0), "c": record.get("c", 0),
		"a": record.get("a", 0.0), "t": record.get("t", 0)}
	var cx: String = record.get("cx", "")
	if cx != "" and _cx_to_net_id.has(cx):
		entry["o"] = _cx_to_net_id[cx]
	return entry

# Host → join-in-progress member: replay the persisted splotch canvas, chunked to stay
# under the relay packet limit. Always sends at least one packet (first=true) so the joiner
# clears its canvas even when nothing has been painted yet. Sent reliable+ordered.
func _send_splotch_sync(net_id: int) -> void:
	const MAX_CHUNK_BYTES := 900  # headroom below the relay MAX_PACKETSIZE (1024)
	var is_first := true
	var i := 0
	var n := _splotch_records.size()

	while true:
		var batch: Array = []
		var packet: PackedByteArray = PackedByteArray()
		while i < n:
			batch.append(_to_wire_splotch(_splotch_records[i]))
			var candidate := _build_splotch_sync_packet(is_first, batch)
			if candidate.size() > MAX_CHUNK_BYTES and batch.size() > 1:
				# This entry pushed the packet over the limit — back it out and flush.
				batch.pop_back()
				break
			packet = candidate
			i += 1

		if packet.is_empty():
			packet = _build_splotch_sync_packet(is_first, batch)

		AppState.bc.relay_service.send(packet, net_id, true, true, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_2)
		is_first = false
		if i >= n:
			break

func _build_splotch_sync_packet(is_first: bool, batch: Array) -> PackedByteArray:
	return JSON.stringify({
		"op": "splotch_sync",
		"data": {"first": is_first, "splotches": batch}
	}).to_utf8_buffer()

# ── Button handlers ───────────────────────────────────────────────────────────

func _on_leave_game_pressed() -> void:
	leave_game.emit()

# ── Player panel ──────────────────────────────────────────────────────────────

func _build_player_panel() -> void:
	for child in _player_panel.get_children():
		child.queue_free()
	_ping_labels.clear()
	_send_to.clear()

	for member: Dictionary in AppState.lobby_members:
		var cx:      String = member.get("cxId", "")
		var is_me:   bool   = cx == AppState.user_cx_id
		var is_host: bool   = cx == AppState.lobby_owner_cx_id

		var row := HBoxContainer.new()
		row.add_theme_constant_override("separation", 4)

		# Color swatch
		var swatch := ColorRect.new()
		swatch.custom_minimum_size = Vector2(12, 12)
		swatch.size_flags_vertical = Control.SIZE_SHRINK_CENTER
		var cidx: int = member.get("extra", {}).get("colorIndex", 0)
		if cidx < AppState.color_palette.size():
			swatch.color = AppState.color_palette[cidx]

		# Mask checkbox (hidden for own row — you always affect yourself locally)
		var cb := CheckBox.new()
		cb.button_pressed = true
		cb.custom_minimum_size = Vector2(20, 18)
		if is_me:
			cb.hide()
		else:
			_send_to[cx] = true
			cb.toggled.connect(func(v: bool): _send_to[cx] = v)

		# Name label with [H] and (me) badges
		var name_text :String = member.get("name", "?")
		if is_host: name_text += " [H]"
		if is_me:   name_text += " (me)"
		var name_lbl := Label.new()
		name_lbl.text = name_text
		name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
		name_lbl.add_theme_font_size_override("font_size", 12)

		# Ping label
		var ping_lbl := Label.new()
		ping_lbl.text = "..."
		ping_lbl.add_theme_font_size_override("font_size", 11)
		ping_lbl.add_theme_color_override("font_color", Color(0.55, 0.55, 0.55))

		row.add_child(swatch)
		row.add_child(cb)
		row.add_child(name_lbl)
		row.add_child(ping_lbl)
		_player_panel.add_child(row)

		if cx != "":
			_ping_labels[cx] = ping_lbl

func _refresh_own_ping() -> void:
	var our_ping: int = AppState.bc.relay_service.get_ping()
	if _ping_labels.has(AppState.user_cx_id):
		(_ping_labels[AppState.user_cx_id] as Label).text = _fmt_ping(our_ping)
	_my_ping_label.text = "● Ping: %s" % _fmt_ping(our_ping)

# ── Cursor management ─────────────────────────────────────────────────────────

func _get_or_create_cursor(net_id: int) -> Node2D:
	if _cursors.has(net_id):
		return _cursors[net_id]
	var cx:     String     = _net_id_to_cx.get(net_id, "")
	var member: Dictionary = _member_for_cx(cx)
	var cursor             = _CURSOR_SCENE.instantiate()
	var pname:  String     = member.get("name", "Player %d" % net_id)
	var extra:  Dictionary = member.get("extra", {})
	var cidx:   int        = extra.get("colorIndex", net_id % maxi(1, AppState.color_palette.size()))
	_game_area.add_child(cursor)  # must be before setup so @onready vars are valid
	cursor.setup(net_id, pname, cidx)
	_cursors[net_id] = cursor
	return cursor

func _remove_cursor_for_cx(cx: String) -> void:
	if not _cx_to_net_id.has(cx):
		return
	var nid: int = _cx_to_net_id[cx]
	if _cursors.has(nid):
		_cursors[nid].queue_free()
		_cursors.erase(nid)
	_net_id_to_cx.erase(nid)
	_cx_to_net_id.erase(cx)

func _member_for_cx(cx: String) -> Dictionary:
	for m: Dictionary in AppState.lobby_members:
		if m.get("cxId", "") == cx:
			return m
	return {}
