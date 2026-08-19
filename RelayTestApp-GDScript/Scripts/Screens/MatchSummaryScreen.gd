# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Post-match results + rematch-queue screen (BCLOUD-14489). Shows how everyone placed this
# round and what it did to their leaderboard ranks. Every player (host included) auto-queues
# via rematch_ready_changed by rematch_ms at the latest, or sooner if they click Queue — no
# separate host-side gate needed, since queuing just calls update_ready(true) and the server
# starts the next round once everyone's ready.
extends Control

signal rematch_ready_changed(ready: bool)
signal main_menu_requested

const _REMATCH_MS := 45000

# How long a player card waits for its leaderboard delta (polled from the GlobalEntity
# PostMatchResults writes — see Main._tick_match_results_poll) before giving up and showing
# "Leaderboard unavailable" instead of "Updating leaderboards..." forever — the cloud script
# call is best-effort, so this is the backstop that keeps this screen from looking
# permanently stuck if it never shows up.
const _LEADERBOARD_RESULT_TIMEOUT_MS := 8000

@onready var _lobby_info_label: Label         = %LobbyInfoLabel
@onready var _winner_label:     Label         = %WinnerLabel
@onready var _cards:            VBoxContainer = %Cards
@onready var _countdown_label:  Label         = %CountdownLabel
@onready var _rematch_button:   Button        = %RematchButton
@onready var _main_menu_button: Button        = %MainMenuButton

var _rematch_ms: int = 0
var _i_am_ready: bool = false
var _clock_ms: float = 0.0
var _ready_count: int = 0
var _total_count: int = 0

var _leaderboard_timeout_handled: bool = false
var _last_entries: Array = []
var _last_lobby_members: Array = []
var _last_local_cx_id: String = ""

func _ready() -> void:
	_rematch_button.pressed.connect(_on_rematch_pressed)
	_main_menu_button.pressed.connect(func(): main_menu_requested.emit())

	init(_REMATCH_MS)
	refresh_results(AppState.match_result_entries, AppState.lobby_members, AppState.user_cx_id)

func _process(delta: float) -> void:
	_clock_ms += delta * 1000.0
	var remaining_ms: int = maxi(0, _rematch_ms - int(_clock_ms))
	var remaining_sec: int = (remaining_ms + 999) / 1000
	_countdown_label.text = "Next Round: %d:%02d" % [remaining_sec / 60, remaining_sec % 60]

	if not _leaderboard_timeout_handled and _clock_ms >= _LEADERBOARD_RESULT_TIMEOUT_MS:
		_leaderboard_timeout_handled = true
		refresh_results(_last_entries, _last_lobby_members, _last_local_cx_id)

	# Per-player auto-queue: if this player hasn't clicked "Queue for Rematch" themselves by
	# rematch_ms, queue them automatically — matches the "if players do nothing, auto rematch"
	# requirement without waiting on anyone else.
	if not _i_am_ready and _clock_ms >= _rematch_ms:
		_i_am_ready = true
		_refresh_rematch_button()
		rematch_ready_changed.emit(true)

# Must be called once, right after instancing, before this screen does anything.
func init(rematch_ms: int) -> void:
	_rematch_ms = rematch_ms
	_clock_ms = 0.0
	_i_am_ready = false

# Rebuilds the winner banner + player cards from the current match result (called once when
# this screen opens, again once the cloud leaderboard results arrive, and again on every
# lobby event while this screen is up so the ready-count stays live).
func refresh_results(entries: Array, lobby_members: Array, local_cx_id: String) -> void:
	_last_entries = entries
	_last_lobby_members = lobby_members
	_last_local_cx_id = local_cx_id

	# Our own row uses the local AppState.user_is_ready instead of lobby_members' entry for us
	# — that entry is briefly stale right after we land here (Main clears it optimistically
	# the moment END_MATCH lands, but the server echo on lobby_members can take a few seconds
	# to catch up), which used to flash e.g. "1/1" before dropping to the correct "0/1".
	_i_am_ready = AppState.user_is_ready

	_total_count = lobby_members.size()
	_ready_count = 0
	for m: Dictionary in lobby_members:
		var is_ready: bool = AppState.user_is_ready if m.get("cxId", "") == local_cx_id else m.get("isReady", false)
		if is_ready:
			_ready_count += 1

	_lobby_info_label.text = "%d Players" % lobby_members.size()

	if entries.size() > 0:
		var winner: Dictionary = entries[0]
		var winner_name := _find_member_name(lobby_members, winner["cx_id"])
		_winner_label.text = "%s wins the round, covering %d%% of the board." % [winner_name, int(round(winner["coverage_pct"]))]
	else:
		_winner_label.text = "Waiting for results..."

	for child in _cards.get_children():
		child.queue_free()
	for entry: Dictionary in entries:
		_cards.add_child(_build_player_card(entry, lobby_members, local_cx_id))

	_refresh_rematch_button()

# ── RTT event handler (called by Main.gd) ─────────────────────────────────────
# Without this, the ready-count/cards drawn in _ready() never update again — another
# player queueing for rematch, leaving, or a membership change all go unnoticed until this
# screen is torn down, which reads as the summary screen being stuck.
func on_lobby_event(_op: String, _data: Dictionary) -> void:
	refresh_results(AppState.match_result_entries, AppState.lobby_members, AppState.user_cx_id)

static func _find_member_name(lobby_members: Array, cx_id: String) -> String:
	for m: Dictionary in lobby_members:
		if m.get("cxId", "") == cx_id:
			return m.get("name", "?")
	return "?"

func _build_player_card(entry: Dictionary, lobby_members: Array, local_cx_id: String) -> PanelContainer:
	var name := "?"
	var color_index := 0
	for m: Dictionary in lobby_members:
		if m.get("cxId", "") == entry["cx_id"]:
			name = m.get("name", "?")
			color_index = int(m.get("extra", {}).get("colorIndex", 0))
			break
	var is_me: bool = entry["cx_id"] == local_cx_id

	var card := PanelContainer.new()
	var card_bg := StyleBoxFlat.new()
	card_bg.bg_color = Color(0.35, 1.0, 0.45, 0.1) if is_me else Color(1, 1, 1, 0.05)
	card_bg.corner_radius_top_left = 10
	card_bg.corner_radius_top_right = 10
	card_bg.corner_radius_bottom_right = 10
	card_bg.corner_radius_bottom_left = 10
	card_bg.content_margin_left = 12
	card_bg.content_margin_right = 12
	card_bg.content_margin_top = 8
	card_bg.content_margin_bottom = 8
	card.add_theme_stylebox_override("panel", card_bg)
	var vbox := VBoxContainer.new()
	card.add_child(vbox)

	var header := HBoxContainer.new()
	header.add_theme_constant_override("separation", 6)

	var rank_lbl := Label.new()
	rank_lbl.text = "#%d" % entry["rank"]
	rank_lbl.add_theme_color_override("font_color", LeaderboardPanel.rank_color(entry["rank"]))

	var dot := ColorRect.new()
	dot.custom_minimum_size = Vector2(10, 10)
	dot.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	if color_index < AppState.color_palette.size():
		dot.color = AppState.color_palette[color_index]

	var name_lbl := Label.new()
	name_lbl.text = name + (" (YOU)" if is_me else "")
	name_lbl.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	if is_me:
		name_lbl.add_theme_color_override("font_color", Color(0.35, 1.0, 0.45))

	var coverage_lbl := Label.new()
	coverage_lbl.text = "%d%%   COVERAGE" % int(round(entry["coverage_pct"]))
	if is_me:
		coverage_lbl.add_theme_color_override("font_color", Color(0.35, 1.0, 0.45))

	header.add_child(rank_lbl)
	header.add_child(dot)
	header.add_child(name_lbl)
	header.add_child(coverage_lbl)
	vbox.add_child(header)

	var beaten: int = entry["beaten"]
	vbox.add_child(_line("+%d pts  (%d beaten + 1 for playing)" % [beaten + 1, beaten]))

	var lb_delta: Dictionary = entry.get("lb_delta", {})
	if not lb_delta.get("ready", false):
		vbox.add_child(_line("Leaderboard unavailable" if _leaderboard_timeout_handled else "Updating leaderboards..."))
	else:
		var points_improved: bool = lb_delta.get("points_lifetime", {}).get("improved", false) \
			or lb_delta.get("points_quarterly", {}).get("improved", false)
		var coverage_improved: bool = lb_delta.get("coverage_lifetime", {}).get("improved", false) \
			or lb_delta.get("coverage_quarterly", {}).get("improved", false)

		if points_improved:
			vbox.add_child(_line("^ Rank up - Opponents Beaten   " + _periods_text(
				lb_delta.get("points_lifetime", {}), lb_delta.get("points_quarterly", {}))))
		if coverage_improved:
			vbox.add_child(_line("* Personal best - Coverage %   " + _periods_text(
				lb_delta.get("coverage_lifetime", {}), lb_delta.get("coverage_quarterly", {}))))
		if not points_improved and not coverage_improved:
			vbox.add_child(_line("No leaderboard rank change this round"))

	return card

static func _line(text: String) -> Label:
	var lbl := Label.new()
	lbl.text = text
	lbl.add_theme_font_size_override("font_size", 12)
	return lbl

# Builds the "Lifetime #before->#after   Quarterly #before->#after" tail for whichever periods
# actually improved — periods that didn't improve are simply omitted.
static func _periods_text(lifetime: Dictionary, quarterly: Dictionary) -> String:
	var parts: Array = []
	if lifetime.get("improved", false):
		var rb: int = lifetime.get("rank_before", -1)
		parts.append("Lifetime #%s -> #%d" % ["-" if rb < 0 else str(rb), lifetime.get("rank_after", -1)])
	if quarterly.get("improved", false):
		var rb2: int = quarterly.get("rank_before", -1)
		parts.append("Quarterly #%s -> #%d" % ["-" if rb2 < 0 else str(rb2), quarterly.get("rank_after", -1)])
	return "   |   ".join(parts)

func _refresh_rematch_button() -> void:
	if _rematch_button == null:
		return
	_rematch_button.text = ("Queued for Rematch" if _i_am_ready else "Queue for Rematch") + "  %d/%d" % [_ready_count, _total_count]
	_rematch_button.modulate = Color(0.4, 0.9, 0.5) if _i_am_ready else Color.WHITE

func _on_rematch_pressed() -> void:
	_i_am_ready = not _i_am_ready
	_refresh_rematch_button()
	rematch_ready_changed.emit(_i_am_ready)
