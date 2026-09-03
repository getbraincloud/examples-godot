extends SceneTree

var _frames := 0
var _ok := true

const _SCENES := [
	"res://Scenes/Main.tscn",
	"res://Scenes/Screens/LobbyScreen.tscn",
	"res://Scenes/Screens/GameScreen.tscn",
	"res://Scenes/Screens/LobbyMember.tscn",
	"res://Scenes/Screens/MatchSummaryScreen.tscn",
]

func _initialize():
	for sp in _SCENES:
		var packed = load(sp)
		if packed == null:
			print("SCENE LOAD FAIL: ", sp)
			_ok = false
			continue
		var inst = packed.instantiate()
		if inst == null:
			print("SCENE INSTANTIATE FAIL: ", sp)
			_ok = false
			continue
		print("loaded+instantiated: ", sp)
		get_root().add_child(inst)

func _process(_delta: float) -> bool:
	_frames += 1
	if _frames > 3:
		print("VERIFY_RESULT: ", "PASS" if _ok else "FAIL")
		quit()
	return false
