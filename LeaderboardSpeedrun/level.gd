extends Node2D
class_name Level

var score = 0.0
static var high_score = 0.0

func _ready():
	score = 0.0

func _process(delta):
	score += delta
	$ScoreLabel.text = "%.1f" % score

func SubmitScore():
	if(score > high_score):
		high_score = score
	get_tree().change_scene_to_file("res://leaderboard_submit.tscn")

func _on_death_area_area_entered(area):
	call_deferred("SubmitScore")
