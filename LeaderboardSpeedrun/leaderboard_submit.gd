extends Node2D

func _on_line_edit_text_submitted(new_text):
	var data = {"nickname": new_text}
	var config = {
		"leaderboardType": "HIGH_VALUE", 
		"rotationType": "NEVER", 
		"retainedCount": 1
	}
	var score: int = roundi(Level.high_score * 10)
	print(score)
	var result = await brainCloud.leaderboard_service.post_score_to_leaderboard_using_config("TimeSurvived", score, data, config)
	if result.status == 200:
		print("Success")
	else:
		print("Failed: %s" % result.status_message)
	
	get_tree().change_scene_to_file("res://level.tscn")
