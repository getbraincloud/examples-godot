# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Shared runtime state accessible by all screens.
extends Node

var bc: BrainCloudWrapper = null

var username: String = ""
var user_cx_id: String = ""

var lobby_id: String = ""
var lobby_members: Array[Dictionary] = []

var color_palette: Array[Color] = []
var lobby_types: Array[String] = []
var splotch_duration: int = -1

var selected_lobby_type: String = ""
var selected_protocol: String = "WS"
var use_ping_data: bool = false
var my_color_index: int = 0

var server_info: Dictionary = {}
var my_net_id: int = -1

var lobby_owner_cx_id: String = ""
var ping_data: Dictionary = {}       # region → ms, measured before lobby join
var game_start_time_ms: int = 0      # ticks_msec when match started (set by host, synced to non-hosts)
