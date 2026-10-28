extends Node2D

func _ready():
	brainCloud.init()

func _on_button_pressed():
	var response = await brainCloud.authenticate_anonymous()
	if response.status == 200:
		print("authentication success")
	else:
		print("authentication failed: ", response.reason_code)
