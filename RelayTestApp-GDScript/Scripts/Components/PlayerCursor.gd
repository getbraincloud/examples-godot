# Copyright 2026 bitHeads, Inc. All Rights Reserved.
# Arrow-shaped cursor for a remote player, coloured by their chosen palette index.
extends Node2D

@onready var _polygon:    Polygon2D = $Polygon2D
@onready var _name_label: Label     = $NameLabel

var net_id:     int    = -1
var player_name: String = ""

func _ready() -> void:
	# Arrow polygon is set in the scene; no override needed here.
	pass

func setup(nid: int, pname: String, color_index: int) -> void:
	net_id      = nid
	player_name = pname
	if _name_label: _name_label.text = pname
	set_color_index(color_index)

func set_color_index(index: int) -> void:
	if index < AppState.color_palette.size(): 
		_polygon.color = AppState.color_palette[index]
	else:
		_polygon.color = Color.WHITE

func move_to(norm_x: float, norm_y: float) -> void:
	position = Vector2(norm_x * 800.0, norm_y * 600.0)
