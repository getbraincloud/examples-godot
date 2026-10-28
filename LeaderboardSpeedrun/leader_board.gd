extends Label

@export var entry_count: int = 10

func _ready():
	var result = await brainCloud.leaderboard_service.get_global_leaderboard_page("TimeSurvived", "HIGH_TO_LOW", 0, entry_count - 1)
	if(result.status == 200):
		for entry in result["data"]["leaderboard"]:
			text += str(entry["data"]["nickname"], " - ", entry["score"] / 10, "\n")
	else:
		print("Failed: %s" % result.status_message)
	
