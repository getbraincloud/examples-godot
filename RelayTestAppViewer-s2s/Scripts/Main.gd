# Copyright 2026 bitHeads, Inc. All Rights Reserved.
extends Control

## Read-only S2S viewer for the CursorParty/RelayTestApp family: a static lobby list on
## the left, and a tabbed panel on the right (Leaderboard / Lobby Info — state, members,
## this-lobby chat, and global chat). No relay connection, no player login, no lobby
## membership — everything here is an S2SContext request (see addons/braincloud_s2s).

var _context: S2SContext = null

var _status_label: Label
var _lobby_list_panel: LobbyListPanel
var _lobby_detail_panel: LobbyDetailPanel
var _global_chat_panel: GlobalChatPanel
var _leaderboard_panel: LeaderboardPanel
var _version_label: Label

func _ready() -> void:
	set_anchors_preset(Control.PRESET_FULL_RECT)

	var root_vbox := VBoxContainer.new()
	root_vbox.set_anchors_preset(Control.PRESET_FULL_RECT)
	add_child(root_vbox)

	_status_label = Label.new()
	_status_label.text = "Connecting..."
	root_vbox.add_child(_status_label)

	var content := HBoxContainer.new()
	content.size_flags_vertical = Control.SIZE_EXPAND_FILL
	content.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	root_vbox.add_child(content)

	_lobby_list_panel = LobbyListPanel.new()
	_lobby_list_panel.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	_lobby_list_panel.lobby_selected.connect(_on_lobby_selected)
	content.add_child(_lobby_list_panel)

	var tabs := TabContainer.new()
	tabs.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	content.add_child(tabs)

	_leaderboard_panel = LeaderboardPanel.new()
	_leaderboard_panel.name = "Leaderboard"
	tabs.add_child(_leaderboard_panel)

	var lobby_info_tab := VBoxContainer.new()
	lobby_info_tab.name = "Lobby Info"
	tabs.add_child(lobby_info_tab)

	# Both panels get an explicit vertical size policy so lobby_info_tab actually splits
	# the available height between them — without this, LobbyDetailPanel's natural height
	# (title + state + members + chat) can grow past the window and push GlobalChatPanel
	# out of the visible area entirely.
	_lobby_detail_panel = LobbyDetailPanel.new()
	_lobby_detail_panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	lobby_info_tab.add_child(_lobby_detail_panel)

	_global_chat_panel = GlobalChatPanel.new()
	_global_chat_panel.size_flags_vertical = Control.SIZE_EXPAND_FILL
	lobby_info_tab.add_child(_global_chat_panel)

	# Persistent version overlay, bottom-left corner — matches every other RelayTestApp
	# client's "App / Client / Server" overlay position (CLAUDE.md: "App + brainCloud
	# Client (+ Server) version shown in a fixed corner on every screen"). Added last so
	# it draws on top of the panels above.
	_version_label = Label.new()
	_version_label.text = "App: %s\nS2S: %s" % [Ids.APP_ID, S2SContext.S2S_VERSION]
	_version_label.set_anchors_preset(Control.PRESET_BOTTOM_LEFT)
	_version_label.grow_horizontal = Control.GROW_DIRECTION_END
	_version_label.grow_vertical = Control.GROW_DIRECTION_BEGIN
	_version_label.modulate = Color(1.0, 1.0, 1.0, 0.6)
	add_child(_version_label)

	_connect.call_deferred()

func _process(_delta: float) -> void:
	# RTT is poll-driven (WebSocketPeer has no signals) — see brainclouds2s_rtt.gd.
	# NOT gated on _rtt_enabled — poll() is what DRIVES the handshake that eventually
	# sets _rtt_enabled true, so gating on it here would mean the connection could never
	# progress past CONNECTING in the first place. poll() itself is a safe no-op before
	# enable_rtt() has created a socket.
	if _context != null:
		_context.get_rtt_service().poll()

func _connect() -> void:
	_context = S2SContext.create(Ids.APP_ID, Ids.SERVER_NAME, Ids.SERVER_SECRET, Ids.S2S_URL, false, self)

	var auth_result := await _context.authenticate()
	if int(auth_result.get("status", -1)) != 200:
		_status_label.text = "Authentication failed: %s" % auth_result.get("status_message", "unknown error")
		return

	_lobby_list_panel.set_context(_context)
	_leaderboard_panel.set_context(_context)
	_lobby_detail_panel.set_context(_context)

	# RTT is needed for both the live lobby-detail channel subscription and global chat.
	_status_label.text = "Authenticated. Connecting RTT for live lobby updates..."
	_context.enable_rtt(func(success: bool, result: Dictionary):
		if success:
			_status_label.text = "Connected."
			_global_chat_panel.set_context(_context)
		else:
			_status_label.text = "RTT connect failed (live lobby-detail updates disabled): %s" % result.get("status_message", "unknown error")
	)

func _on_lobby_selected(lobby_id: String) -> void:
	_lobby_detail_panel.show_lobby(lobby_id)
