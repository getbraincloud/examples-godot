extends Node2D

var fall_speed = 400.0

func _process(delta):
	position.y += fall_speed * delta

func _on_area_2d_body_entered(body):
	if(body.name == "Player"):
		queue_free()
