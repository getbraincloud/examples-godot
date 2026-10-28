extends Node2D

var ball_obj = preload("res://ball.tscn")
var speed = 400

func _on_timer_timeout():
	var ball_ref = ball_obj.instantiate() as Node2D
	add_child(ball_ref)
	ball_ref.global_position = global_position
	ball_ref.position.x = randf_range(-300, 300)
	ball_ref.fall_speed = speed
	
	speed += 5
