using BrainCloud;
using BrainCloud.JsonFx.Json;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

public partial class Main : Node
{
	// Visuals
	// 40-colour palette aligned with the Java/C#/JS/C++ CursorParty clients. Overwritten at
	// runtime from the "Colours" global property (see OnGetColoursCallback); this is the default.
	public static Color[] Colours =
	{
		// Row 0 — vivid
		new Color("#FF3333"), new Color("#FF8800"), new Color("#FFD700"), new Color("#88FF00"), new Color("#00EE44"),
		new Color("#00DDDD"), new Color("#00AAFF"), new Color("#3355FF"), new Color("#AA00FF"), new Color("#FF00BB"),
		// Row 1 — vivid-medium
		new Color("#FF5566"), new Color("#FFAA00"), new Color("#AADD00"), new Color("#00FF88"), new Color("#00FFCC"),
		new Color("#0088FF"), new Color("#8833FF"), new Color("#FF44AA"), new Color("#77FF33"), new Color("#FF6688"),
		// Row 2 — pastel
		new Color("#FF9999"), new Color("#FFCC88"), new Color("#FFFF88"), new Color("#AAFFAA"), new Color("#88FFEE"),
		new Color("#AABBFF"), new Color("#DDBBFF"), new Color("#FFBBDD"), new Color("#CCFFDD"), new Color("#FFEECC"),
		// Row 3 — muted
		new Color("#CC1133"), new Color("#CC5500"), new Color("#88AA00"), new Color("#228855"), new Color("#009999"),
		new Color("#3366AA"), new Color("#7744CC"), new Color("#AA3366"), new Color("#AA6633"), new Color("#7788AA")
	};
	private string splatterScene = "Scenes/Splatter.tscn";
	private float splatterLifespan;
	private float splatterAppear;
	private float splatterDisappear;

	// Leaderboard ids — overridable via matching global properties (Design > Global
	// Properties) so the boards can be created/renamed in the portal with no client rebuild;
	// these are just the compiled-in defaults, matching cpp's globals.h. See
	// GetSplatterProperties/OnGetLeaderboardIdsCallback.
	private string pointsLeaderboardId = "CursorParty_Points";
	private string pointsLeaderboardIdQuarterly = "CursorParty_Points_Quarterly";
	private string coverageLeaderboardId = "CursorParty_HighestCoverage";
	private string coverageLeaderboardIdQuarterly = "CursorParty_HighestCoverage_Quarterly";

	// Screens / Scenes
	private AuthenticationScreen _authenticationScreen;
	private LoadingScreen _loadingScreen;
	private ErrorScreen _errorScreen;
	private LobbySelectScreen _lobbySelectScreen;
	private LobbyScreen _lobbyScreen;
	private MatchScreen _matchScreen;
	private MatchSummaryScreen _matchSummaryScreen;
	private CursorParty _cursorParty;

	// Scene Data
	private MarginContainer _sceneContainer;
	private Node _currentScene;

	// Game Data
	private string _gameMode = null;
	private string _teamCode = null;
	private bool _presentWhileStarted = false;
	private bool _matchmaking = false;
	private long _matchmakingStartMs = 0;
	private bool _matchmakingCancelled = false;
	private bool _playing = false;
	private string _lobbyID = null;
	private Dictionary<string, object> _lobby = null;
	private Dictionary<string, object> _server = null;
	private List<Member> _matchMembers = new List<Member>();

	// This-lobby chat history (Lobby service SendSignal, separate from the Chat-service global
	// channel). Kept here rather than on LobbyScreen so it survives GoToLobbyScreen() recreating
	// the screen every time we return to the lobby after a match; only cleared on a fresh
	// matchmaking or leaving the lobby.
	private readonly List<(string fromName, string text, bool isMe)> _lobbyChatHistory = new();

	// Relay transport protocol selected on the lobby select screen (WEBSOCKET / TCP / UDP)
	private RelayConnectionType _relayConnectionType = RelayConnectionType.WEBSOCKET;

	// When true, ping regions before matchmaking so brainCloud places us in the
	// lowest-latency region (FindOrCreateLobbyWithPingData). Toggled on the lobby select screen.
	private bool _usePingData = false;

	// This client's region ping times (region -> ms), captured from PingRegions and shared
	// with the lobby via extra["pings"] so every member can display everyone's ping data.
	private Dictionary<string, int> _pingData = new Dictionary<string, int>();

	// Relay send options, driven by the in-match Settings HUD (mirrors the dotnet RelayTestApp).
	// Movement sends use these flags; shockwaves are always reliable/unordered but honour the
	// channel + the per-player send mask below.
	private bool _sendReliable = false;
	private bool _sendOrdered  = true;
	private int  _sendChannel  = BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1;

	// Per-member "send to this player" mask (cxId -> allowed), driving SendToPlayers for
	// shockwaves. Default: everyone except ourselves (a sender never receives its own message).
	private readonly Dictionary<string, bool> _allowSendTo = new Dictionary<string, bool>();

	// Live relay-server RTT per member (cxId -> ms), broadcast to all players every 2s during a
	// match (op "relay_ping") and shown next to each player in the HUD. -1 = not yet received.
	private readonly Dictionary<string, int> _activePing = new Dictionary<string, int>();
	private double _pingBroadcastAccum = 0;

	// App-configured lobby types, read from the "AllLobbyTypes" global property (not hardcoded).
	private List<string> _appLobbies = new List<string>();

	// Persisted splotch canvas for the current match. The host replays this to a
	// join-in-progress member (via SendToPlayers) so late joiners see the splotches
	// that were created before they connected.
	private class SplotchRecord { public float X; public float Y; public int ColorIndex; public float Angle; public long StartTimeMs; public string CxId; }
	private readonly List<SplotchRecord> _splotches = new List<SplotchRecord>();

	// Live coverage recompute (feeds the in-match scoreboard sidebar) — see Coverage.cs.
	private List<Coverage.Entry> _coverage = new();
	private double _coverageRecomputeAccum = 0;
	private const double CoverageRecomputeSec = 0.25;

	// Match-end sequencing (mirrors the cpp/react "tickMatch" auto-end flow): the host computes
	// final coverage, broadcasts "match_result", waits ResultGraceMs, then ends the match.
	private enum MatchPhase { Running, ResultsBroadcast, Ended }
	private MatchPhase _matchPhase = MatchPhase.Running;
	private int _roundNumber = 0;
	private long _matchStartTimeMs = 0;
	private const long MatchDurationMs = 90000;
	private const long ResultGraceMs = 3000;
	private long _resultsSentAtMs = 0;

	// LeaderboardPeriodDelta / LeaderboardDelta / MatchResultEntry / MatchResultData are
	// defined in MatchResult.cs (public — MatchSummaryScreen needs them too).
	private MatchResultData _matchResult = new();
	private int _leaderboardPostedRound = -1;
	private List<MatchResultEntry> _pendingMatchResult = new();
	private readonly List<(string CxId, LeaderboardDelta Delta)> _pendingLbChunk = new();
	private readonly Dictionary<string, LeaderboardDelta> _pendingLbResults = new();

	// Match Summary / rematch flow (BCLOUD-14489).
	private bool _awaitingRematch = false;
	private long _matchSummaryArrivalTimeMs = 0;
	private const long MatchSummaryRematchMs = 45000;

	// Non-blocking "starting the next round" status — the Lobby screen stays up (chat/etc.
	// still usable) through the whole STARTING -> ROOM_READY provisioning sequence instead of
	// jumping to a blocking LoadingScreen.
	private bool _isProvisioning = false;
	private string _provisioningStatus = "";

	// User Data
	private string _userProfileID = null;
	private string _userCxID = null;
	private string _username = null;
	private int _userColourIndex = 0;
	private bool _userIsReady = false;
	private bool _userWasPresentSinceStart = false;

	// brainCloud data
	private BrainCloudWrapper _brainCloudWrapper;

	// Used to determine OnAuthenticateFailed error message
	private bool _reconnecting;

	// Queued callers waiting for RTT to finish enabling (mirrors the dotnet/react RTAs'
	// "ensureRTTEnabled" wrapper). EnableRTT() silently no-ops — no callback either way —
	// if it's already enabled or still connecting, so go through here instead of calling it
	// directly: the main menu enables RTT early for global chat, and if Match Make gets
	// clicked before that resolves, a second raw EnableRTT call would just drop
	// OnMatchMakingRequested's callback.
	private readonly List<(Action onReady, Action<int, int, string> onFail)> _rttEnableWaiters = new();
	private bool _rttEnabling = false;

	private void EnsureRttEnabled(Action onReady, Action<int, int, string> onFail)
	{
		if (_brainCloudWrapper.RTTService.IsRTTEnabled())
		{
			onReady();
			return;
		}

		_rttEnableWaiters.Add((onReady, onFail));
		if (_rttEnabling) return;
		_rttEnabling = true;

		_brainCloudWrapper.RTTService.EnableRTT(
			(jsonResponse, cbObject) =>
			{
				_userCxID = _brainCloudWrapper.RTTService.getRTTConnectionID();
				_rttEnabling = false;
				var waiters = new List<(Action onReady, Action<int, int, string> onFail)>(_rttEnableWaiters);
				_rttEnableWaiters.Clear();
				foreach (var w in waiters) w.onReady();
			},
			(status, reasonCode, jsonError, cbObject) =>
			{
				_rttEnabling = false;
				var waiters = new List<(Action onReady, Action<int, int, string> onFail)>(_rttEnableWaiters);
				_rttEnableWaiters.Clear();
				foreach (var w in waiters) w.onFail(status, reasonCode, jsonError);
			});
	}

	public override void _Ready()
	{
		_sceneContainer = GetNode<MarginContainer>("ScreenContainer");

		StartApp();
	}

	public override void _Process(double delta)
	{
		// Make sure you invoke this method in Update, or else you won't get any callbacks
		_brainCloudWrapper.RunCallbacks();

		// Broadcast our live relay RTT to all players every 2 seconds while in a match.
		if (_playing)
		{
			_pingBroadcastAccum += delta;
			if (_pingBroadcastAccum >= 2.0)
			{
				_pingBroadcastAccum = 0;
				BroadcastRelayPing();
			}
		}

		// Live coverage recompute + the host's auto-end-at-MatchDurationMs sequence.
		if (_playing)
		{
			_coverageRecomputeAccum += delta;
			if (_coverageRecomputeAccum >= CoverageRecomputeSec)
			{
				_coverageRecomputeAccum = 0;
				TickMatch();
			}
		}

		// Host-only: keeps evaluating whether to auto-start the next round even while sitting
		// on the Lobby (rather than the Match Summary screen).
		if (_awaitingRematch) TickRematchGate();
	}

	public override void _Notification(int what)
	{
		if(what == NotificationWMCloseRequest)
		{
			_brainCloudWrapper.Logout(false);
			GetTree().Quit(); // default behaviour
		}
	}

	/// <summary>
	/// Reset all scenes/screens, initialize brainCloud and display authentication screen.
	/// </summary>
	private void StartApp()
	{
		// Reset scenes/screens
		if (_currentScene != null)
		{
			_currentScene.QueueFree();
		}

		_authenticationScreen = null;
		_lobbySelectScreen = null;
		_lobbyScreen = null;
		_matchScreen = null;
		_matchSummaryScreen = null;
		_loadingScreen = null;
		_errorScreen = null;

		// Display loading screen
		LoadScene("Initializing brainCloud");
		
		// Initialize brainCloud
		_brainCloudWrapper = new BrainCloudWrapper();
		_brainCloudWrapper.Init(Ids._url, Ids._appSecret, Ids._appID, Ids._version);

		// Persistent version overlay (always visible, every screen) — App + brainCloud client version.
		var versionOverlay = GetNodeOrNull<Label>("VersionOverlay");
		if (versionOverlay != null)
			versionOverlay.Text = $"App: {Ids._version}  |  Client: {BrainCloud.Version.GetVersion()}";

		if (!_brainCloudWrapper.Client.Initialized)
		{
			OnError("Failed to initialize brainCloud. Verify appID, app secret, and URL");
		}
		else
		{
			_brainCloudWrapper.Client.EnableLogging(true);

			// Check for saved profile/anonymous IDs - reconnect returning user or prompt new user to login accordingly
			if (_brainCloudWrapper.CanReconnect())
			{
				LoadScene("Reconnecting . . .");
				_reconnecting = true;
				_brainCloudWrapper.Reconnect(OnAuthenticationSuccess, OnAuthenticationFailed);
			}
			else
			{
				GoToAuthenticationScreen();
			}
		}
	}

	/// <summary>
	/// Display loading screen and message indicating next expected screen/scene.
	/// </summary>
	/// <param name="loadingScreenMsg">string message indicating what is being loaded.</param>
	/// <param name="cancellable">Show the elapsed-time readout + a working Cancel button —
	/// only for the matchmaking-search steps (mirrors cpp's canCancel / JS's 'joiningLobby').</param>
	private void LoadScene(string loadingScreenMsg, bool cancellable = false)
	{
		// Create new LoadingScreen
		var loadingScreenScene = GD.Load<PackedScene>("res://Scenes/LoadingScreen.tscn");
		_loadingScreen = (LoadingScreen)loadingScreenScene.Instantiate();

		// Transition to loading screen
		ChangeScene(_loadingScreen);

		// Display loading screen message
		_loadingScreen.SetLoadingMessage(loadingScreenMsg);

		if (cancellable)
		{
			_loadingScreen.EnableTimerAndCancel(_matchmakingStartMs);
			_loadingScreen.Connect(LoadingScreen.SignalName.CancelRequested, new Callable(this, MethodName.OnMatchmakingCancelRequested));
		}

		// Reset other Scenes
		_authenticationScreen = null;
		_lobbySelectScreen = null;
		_lobbyScreen = null;
		_matchScreen = null;
		_matchSummaryScreen = null;
		_playing = false;

	}

	/// <summary>
	/// User cancelled the matchmaking search (Cancel button on the loading screen). Tears
	/// down RTT/lobby state the same way a failed match attempt does and returns to the
	/// lobby-select screen — mirrors GDScript's _on_cancel_search_pressed.
	/// </summary>
	private void OnMatchmakingCancelRequested()
	{
		_matchmakingCancelled = true;
		_matchmaking = false;
		_brainCloudWrapper.RTTService.DeregisterRTTLobbyCallback();
		_brainCloudWrapper.RTTService.DisableRTT();
		_lobbyID = null;
		_lobby = null;
		GoToLobbySelectScreen();
	}

	/// <summary>
	/// Switch to a new screen/scene, discarding the previous one.
	/// </summary>
	/// <param name="scene">Node containing the new screen/scene.</param>
	private void ChangeScene(Node scene)
	{
		if (_currentScene != null)
		{
			_currentScene.QueueFree();
		}
		_currentScene = scene;

		_sceneContainer.AddChild(_currentScene);
	}

	/// <summary>
	/// Display authentication screen.
	/// </summary>
	private void GoToAuthenticationScreen()
	{
		var authenticationScene = GD.Load<PackedScene>("res://Scenes/AuthenticationScreen.tscn");
		_authenticationScreen = (AuthenticationScreen)authenticationScene.Instantiate();

		// Transition to authentication scene
		ChangeScene(_authenticationScreen);

		// Connect event listener(s)
		_authenticationScreen.Connect(AuthenticationScreen.SignalName.AuthenticationRequested, new Callable(this, MethodName.OnAuthenticationRequested));
	}

	/// <summary>
	/// Display the lobby select / pre-lobby screen.
	/// </summary>
	private void GoToLobbySelectScreen()
	{
		var lobbySelectScene = GD.Load<PackedScene>("res://Scenes/LobbySelectScreen.tscn");
		_lobbySelectScreen = (LobbySelectScreen)lobbySelectScene.Instantiate();

		// Transition to lobby select scene
		ChangeScene(_lobbySelectScreen);

		_lobbySelectScreen.SetNameLabel(_username);

		// Populate the lobby-type dropdown from the app-configured list (AllLobbyTypes global
		// property). Falls back to the scene's default items if it hasn't loaded yet.
		_lobbySelectScreen.SetLobbyTypes(_appLobbies);

		_lobbySelectScreen.InitExtraPanels(_brainCloudWrapper);

		// Keep RTT connected from the main menu onward so Global Chat works there (brainCloud's
		// chat calls require RTT). OnMatchMakingRequested's EnsureRttEnabled call resolves
		// immediately once this has already succeeded.
		EnsureRttEnabled(() => { }, (status, reasonCode, jsonError) => { });

		// Connect event listener(s)
		_lobbySelectScreen.Connect(LobbySelectScreen.SignalName.LogOutRequested, new Callable(this, MethodName.OnLogOutRequested));
		_lobbySelectScreen.Connect(LobbySelectScreen.SignalName.MatchMakingRequested, new Callable(this, MethodName.OnMatchMakingRequested));
	}

	/// <summary>
	/// Usually just ownerCxId == us, but _lobby can go stale (some events don't carry a fresh
	/// lobby object) and leave ownerCxId pointing at someone who already left. If we're the only
	/// member left, treat ourselves as host regardless — otherwise a solo player after
	/// Queue-for-Rematch never sees a Start button.
	/// </summary>
	private bool IsEffectivelyHost()
	{
		if (_lobby == null || !_lobby.ContainsKey("ownerCxId")) return false;
		if ((string)_lobby["ownerCxId"] == _userCxID) return true;

		if (_lobby.ContainsKey("members") && _lobby["members"] is Dictionary<string, object>[] members
			&& members.Length == 1 && members[0].ContainsKey("cxId"))
		{
			return (string)members[0]["cxId"] == _userCxID;
		}

		return false;
	}

	/// <summary>
	/// Display the lobby screen.
	/// </summary>
	private void GoToLobbyScreen()
	{
		var lobbyScene = GD.Load<PackedScene>("res://Scenes/LobbyScreen.tscn");
		_lobbyScreen = (LobbyScreen)lobbyScene.Instantiate();

		// Clear the other screen refs — ChangeScene() only frees _currentScene, so without this
		// a stale reference to a freed node lingers and throws ObjectDisposedException the next
		// time some other callback (e.g. an RTT lobby event) touches it via a "!= null" guard.
		_matchScreen = null;
		_matchSummaryScreen = null;

		// Transition to lobby scene
		ChangeScene(_lobbyScreen);

		// Give the screen our own ping data + cxId so our row shows immediately, even before
		// the lobby echoes our extra["pings"] back to us.
		_lobbyScreen.SetLocalPingData(_pingData, _userCxID);

		_lobbyScreen.UpdateLobbyMembers(_lobby);

		_lobbyScreen.ToggleStartButtonVisibility(IsEffectivelyHost());
		_lobbyScreen.SetReadyButtonState(_userIsReady);

		_matchMembers.Clear();

		_lobbyScreen.InitExtraPanels(_brainCloudWrapper);
		_lobbyScreen.SetLobbyChatHistory(_lobbyChatHistory);
		_lobbyScreen.SetProvisioning(_isProvisioning, _provisioningStatus);

		// Connect even listener(s)
		_lobbyScreen.Connect(LobbyScreen.SignalName.ColourChanged, new Callable(this, MethodName.OnColourChanged));
		_lobbyScreen.Connect(LobbyScreen.SignalName.LeaveLobbyRequested, new Callable(this, MethodName.OnLeaveLobbyRequested));
		_lobbyScreen.Connect(LobbyScreen.SignalName.JoinMatchRequested, new Callable(this, MethodName.OnJoinMatchRequested));
		_lobbyScreen.Connect(LobbyScreen.SignalName.StartMatchRequested, new Callable(this, MethodName.OnStartMatchRequested));
		_lobbyScreen.Connect(LobbyScreen.SignalName.ReadyToggleRequested, new Callable(this, MethodName.OnReadyToggleRequested));
		_lobbyScreen.Connect(LobbyScreen.SignalName.LobbySignalSendRequested, new Callable(this, MethodName.OnLobbySignalSendRequested));
	}

	/// <summary>
	/// Display the match screen.
	/// </summary>
	private void GoToMatchScreen()
	{
		var matchScene = GD.Load<PackedScene>("res://Scenes/MatchScreen.tscn");
		_matchScreen = (MatchScreen)matchScene.Instantiate();

		// See the matching comment in GoToLobbyScreen — clear sibling screen refs so a stale,
		// freed-but-non-null reference can't get touched by a later callback.
		_lobbyScreen = null;
		_matchSummaryScreen = null;

		// Transittion to match scene
		ChangeScene(_matchScreen);

		// Seed the per-player send mask (everyone but us) and give the match HUD the state it
		// needs to render the Players mask + the send-option controls.
		EnsureSendMaskDefaults();
		_matchScreen.SetSendMaskState(_allowSendTo, _userCxID);
		_matchScreen.SetSendOptions(_sendReliable, _sendOrdered, _sendChannel);

		_matchScreen.UpdateLobbyMembers(_lobby);

		_cursorParty = GetNode<CursorParty>("ScreenContainer/MatchScreen/CenterContainer/MatchContainer/GameSide/GameArea/CursorParty");
		_cursorParty.SetUserColourIndex(_userColourIndex);

		// Start each match with a fresh splotch canvas (the host replays it to JIP members).
		_splotches.Clear();

		// Reset live-ping state for the new match (rows show "..." until first broadcast).
		_activePing.Clear();
		_pingBroadcastAccum = 0;

		// Fresh round — reset per-round match/coverage state so nothing carries over from the
		// previous round.
		_roundNumber++;
		_matchStartTimeMs = NowMs();
		_coverage = new List<Coverage.Entry>();
		_matchResult = new MatchResultData();
		_matchPhase = MatchPhase.Running;
		_resultsSentAtMs = 0;
		_coverageRecomputeAccum = 0;
		_leaderboardPostedRound = -1;
		_pendingMatchResult = new List<MatchResultEntry>();
		_pendingLbChunk.Clear();
		_pendingLbResults.Clear();
		_awaitingRematch = false;
		_isProvisioning = false;

		_playing = true;

		// Connect event listener(s)
		_matchScreen.Connect(MatchScreen.SignalName.LeaveMatchRequested, new Callable(this, MethodName.OnLeaveMatchRequested));
		_matchScreen.Connect(MatchScreen.SignalName.SendReliableChanged, new Callable(this, MethodName.OnSendReliableChanged));
		_matchScreen.Connect(MatchScreen.SignalName.SendOrderedChanged, new Callable(this, MethodName.OnSendOrderedChanged));
		_matchScreen.Connect(MatchScreen.SignalName.SendChannelChanged, new Callable(this, MethodName.OnSendChannelChanged));
		_matchScreen.Connect(MatchScreen.SignalName.SendMaskChanged, new Callable(this, MethodName.OnSendMaskChanged));

		_cursorParty.Connect(CursorParty.SignalName.MouseMoved, new Callable(this, MethodName.OnUserMoved));
		_cursorParty.Connect(CursorParty.SignalName.MouseClicked, new Callable(this, MethodName.OnUserClicked));
	}

	/// <summary>
	/// Display the post-match results + rematch-queue screen (BCLOUD-14489).
	/// </summary>
	private void GoToMatchSummaryScreen()
	{
		var scene = GD.Load<PackedScene>("res://Scenes/MatchSummaryScreen.tscn");
		_matchSummaryScreen = (MatchSummaryScreen)scene.Instantiate();

		// See the matching comment in GoToLobbyScreen — clear sibling screen refs so a stale,
		// freed-but-non-null reference can't get touched by a later callback.
		_lobbyScreen = null;
		_matchScreen = null;

		ChangeScene(_matchSummaryScreen);

		_matchSummaryScreen.Init(_matchSummaryArrivalTimeMs, MatchSummaryRematchMs);
		_matchSummaryScreen.RefreshResults(_matchResult.Entries, _lobby, _userCxID, _userIsReady);

		_matchSummaryScreen.Connect(MatchSummaryScreen.SignalName.RematchReadyChanged, new Callable(this, MethodName.OnSetRematchReady));
		_matchSummaryScreen.Connect(MatchSummaryScreen.SignalName.MainMenuRequested, new Callable(this, MethodName.OnLeaveLobbyRequested));
	}

	/// <summary>
	/// Connect to Relay Server using data received from "ROOM_READY" lobby event.
	/// </summary>
	private void ConnectToRelayServer()
	{
		// No LoadScene here — we stay on the Lobby screen with the provisioning banner up
		// (see the STARTING/ROOM_READY handling above) instead of a blocking loading screen.
		// GoToMatchScreen below is what actually changes screens, once relay connects.

		// Verify that server data was received/set before attempting to connect
		if (_server == null)
		{
			OnError("Unexpected error finding server data.");

			return;
		}

		Dictionary<string, object> connectData = (Dictionary<string, object>)_server["connectData"];
		Dictionary<string, object> ports = (Dictionary<string, object>)connectData["ports"];

		_presentWhileStarted = false;

		_brainCloudWrapper.RelayService.RegisterRelayCallback(OnRelayMessageReceived);
		_brainCloudWrapper.RelayService.RegisterSystemCallback(OnSystemMessageReceived);

		// Resolve the connection type + port from the protocol selected on the lobby
		// select screen. GameLift and i3D only expose a single WebSocket port, so force
		// WEBSOCKET when those are present (matches the other CursorParty examples).
		RelayConnectionType relayConnectionType;
		int port;
		if (ports.ContainsKey("gamelift") && ports["gamelift"] != null)
		{
			relayConnectionType = RelayConnectionType.WEBSOCKET;
			port = Convert.ToInt32(ports["gamelift"]);
		}
		else if (ports.ContainsKey("i3d") && ports["i3d"] != null)
		{
			relayConnectionType = RelayConnectionType.WEBSOCKET;
			port = Convert.ToInt32(ports["i3d"]);
		}
		else
		{
			relayConnectionType = _relayConnectionType;
			string portKey = _relayConnectionType switch
			{
				RelayConnectionType.TCP => "tcp",
				RelayConnectionType.UDP => "udp",
				_                       => "ws"
			};
			port = Convert.ToInt32(ports[portKey]);
		}

		GD.Print($"Connecting to relay server via {relayConnectionType} on port {port}");

		bool ssl = false;
		string host = (string)connectData["address"];
		string serverPasscode = (string)_server["passcode"];
		string lobbyID = (string)_server["lobbyId"];

		RelayConnectOptions relayConnectOptions = new RelayConnectOptions(ssl, host, port, serverPasscode, lobbyID);

		SuccessCallback OnRelayConnectSuccess = (jsonResponse, cbObject) =>
		{
			GD.Print(string.Format("RelayConnect Success:\n{0}", jsonResponse));

			GoToMatchScreen();
		};

		FailureCallback OnRelayConnectFailed = (status, reasonCode, jsonError, cbObject) =>
		{
			GD.Print(string.Format("RelayConnect Failed:\n{0}  {1}  {2}", status, reasonCode, jsonError));
		};

		_brainCloudWrapper.RelayService.Connect(relayConnectionType, relayConnectOptions, OnRelayConnectSuccess, OnRelayConnectFailed);
	}

	/// <summary>
	/// Convert a JSON string to a Dictionary.
	/// </summary>
	/// <param name="in_data">Data to be converted.</param>
	/// <returns></returns>
	private Dictionary<string, object> DeserializeString(byte[] in_data)
	{
		Dictionary<string, object> toDict = new Dictionary<string, object>();
		string jsonMessage = Encoding.ASCII.GetString(in_data);
		if (jsonMessage == "") return toDict;

		try
		{
			toDict = (Dictionary<string, object>)BrainCloud.JsonFx.Json.JsonReader.Deserialize(jsonMessage);
		}
		catch (Exception)
		{
			GD.Print("Could not serialize: " + jsonMessage);
		}
		return toDict;
	}

	private void SetMemberPosition(Dictionary<string, object> lobbyMember, Vector2 position)
	{
		string memberCxID = (string)lobbyMember["cxId"];

		// Normalize position coordinates for an accurate position across different platforms/resolutions/screens
		float xCoord = position.X;
		float yCoord = position.Y;
		xCoord *= 800;
		yCoord *= 600;

		if (memberCxID == _userCxID)
		{
			return;
		}

		if(_cursorParty != null && _playing)
		{
			foreach(Member matchMember in _matchMembers)
			{
				if(memberCxID == matchMember.GetCxID())
				{
					GD.Print("Found member");
					matchMember.Position = new Vector2(xCoord, yCoord);

					return;
				}
			}

			// If no match was found, create a new Member instance
			GD.Print("Creating new Member");

			var memberScene = GD.Load<PackedScene>("Scenes/Member.tscn");
			Member newMember = (Member)memberScene.Instantiate();
			_cursorParty.AddChild(newMember);
			_matchMembers.Add(newMember);

			Dictionary<string, object> extra = (Dictionary<string, object>)lobbyMember["extra"];

			newMember.Position = new Vector2(xCoord, yCoord);
			newMember.SetColour((int)extra["colorIndex"]);
			newMember.SetName((string)lobbyMember["name"]);
			newMember.SetCxID(memberCxID);
		}
	}

	/// <summary>
	/// Create a Splatter instance.
	/// </summary>
	/// <param name="pos"></param>
	/// <param name="colourIndex"></param>
	private void CreateSplatter(Vector2 pos, int colourIndex, float angle)
	{
		// Normalize Splatter position coordinates for an accurate position across different platforms/resolutions/screens
		float xCoord = pos.X;
		float yCoord = pos.Y;
		xCoord *= 800;
		yCoord *= 600;

		// Expanding shockwave ring at the impact point — mirrors the GDScript/C++ clients,
		// which draw a ring alongside the paint splotch (this app was missing it entirely).
		var shockwave = new Shockwave();
		_cursorParty.GetNode("SplatterMask").AddChild(shockwave);
		shockwave.Position = new Vector2(xCoord, yCoord);
		shockwave.Setup(Colours[colourIndex]);

		// Create new Splatter and add it to the list
		var paintSplatter = GD.Load<PackedScene>(splatterScene);
		Splatter newSplatter = (Splatter)paintSplatter.Instantiate();
		_cursorParty.GetNode("SplatterMask").AddChild(newSplatter);
		newSplatter.Position = new Vector2(xCoord, yCoord);
		newSplatter.SetColour(Colours[colourIndex]);
		newSplatter.SetAngle(angle); // network-synced rotation so all clients match
		newSplatter.SetLifespan(splatterLifespan);
		newSplatter.SetAnimationDurations(splatterAppear, splatterDisappear);
	}

	/// <summary>
	/// Display error screen and brief message describing the error.
	/// </summary>
	/// <param name="errorMsg">string message describing the error.</param>
	private void OnError(string errorMsg)
	{
		// Create new ErrorScreen
		var errorScreenScene = GD.Load<PackedScene>("res://Scenes/ErrorScreen.tscn");
		_errorScreen = (ErrorScreen)errorScreenScene.Instantiate();
		ChangeScene(_errorScreen);

		// Display error message
		_errorScreen.SetErrorMessage(errorMsg);

		// Connect event listener(s)
		_errorScreen.Connect(ErrorScreen.SignalName.ErrorMessageDismissed, new Callable(this, MethodName.OnErrorMessageDismissed));
	}

	/// <summary>
	/// Restart the application.
	/// </summary>
	private void OnErrorMessageDismissed()
	{
		StartApp();
	}

	/// <summary>
	/// Attempt to authenticate user with brainCloud.
	/// TODO:  implement other forms of authentication. For now, AuthenticateUniversal is the only option.
	/// </summary>
	/// <param name="id">string containing the user's ID. Right now this is a universal ID.</param>
	/// <param name="token">string containing the user's authentication token. Right now this is a Password.</param>
	private void OnAuthenticationRequested(string id, string token)
	{
		// TODO:  make this optional for the user
		bool forceCreate = true;

		if (_brainCloudWrapper.Client.IsAuthenticated())
		{
			OnError("User already authenticated");
		}
		else
		{
			LoadScene("Authenticating . . .");

			_username = id;

			_brainCloudWrapper.AuthenticateUniversal(id, token, forceCreate, OnAuthenticationSuccess, OnAuthenticationFailed);
		}
	}

	/// <summary>
	/// Update the user's brainCloud "player name" and then proceed to the LobbySelect Scene/Screen.
	/// </summary>
	/// <param name="jsonResponse">The JSON response from the server.</param>
	/// <param name="cbObject">The user supplied callback object.</param>
	private void OnAuthenticationSuccess(string jsonResponse, object cbObject)
	{
		GD.Print(string.Format("Authentication Success:\n{0}", jsonResponse));

		LoadScene("Getting player info . . .");

		var response = BrainCloud.JsonFx.Json.JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
		var data = response["data"] as Dictionary<string, object>;
		string profileID = data["profileId"] as string;

		// Update Player State playerName
		SuccessCallback ReadUserStateSuccess = (response, cbObject) =>
		{
			if (_brainCloudWrapper.Client.LoggingEnabled)
			{
				GD.Print("ReadUserState Success: " + response);
			}

			var readUserStateResponse = BrainCloud.JsonFx.Json.JsonReader.Deserialize<Dictionary<string, object>>(response);
			var data = readUserStateResponse["data"] as Dictionary<string, object>;
			string currentPlayerName = data["playerName"] as string;

			// _username will be null during reconnect
			if (string.IsNullOrEmpty(_username))
			{
				_username = currentPlayerName;

				// Proceed to pre-lobby / lobby select
				GoToLobbySelectScreen();
			}

			// _username will have a value during normal authentication
			else
			{
				// The playerName property will need to be updated if a returning user logs in with a mismatched Universal ID
				if (!_username.Equals(currentPlayerName))
				{
					// Callbacks for UpdateName
					SuccessCallback OnUpdateNameSuccess = (response, cbObject) =>
					{
						GD.Print(string.Format("Update Name Success:\n{0}", response));

						_userProfileID = profileID;

						// Proceed to pre-lobby / lobby select
						GoToLobbySelectScreen();
					};

					FailureCallback OnUpdateNameFailed = (status, code, error, cbObject) =>
					{
						GD.Print(string.Format("Update Name Failed:\n{0}  {1}  {2}", status, code, error));

						OnAuthenticationFailed(status, code, error, cbObject);
					};

					// TODO:  this will need to be modified if/when other authentication methods are implemented
					// Update player name with ID used to log in
					_brainCloudWrapper.PlayerStateService.UpdateUserName(_username, OnUpdateNameSuccess, OnUpdateNameFailed);
				}
				else {

					// Proceed to pre-lobby / lobby select
					GoToLobbySelectScreen();
				}
			}
		};

		FailureCallback ReadUserStateFailed = (status, code, error, cbObject) =>
		{
			GD.Print(string.Format("ReadUserState Failed:\n{0} {1} {2}", status, code, error));

			OnAuthenticationFailed(status, code, error, cbObject);
		};

		_brainCloudWrapper.PlayerStateService.ReadUserState(ReadUserStateSuccess, ReadUserStateFailed);

		// Read global properties to determine the values that should be used for splatter visuals
		GetSplatterProperties();
	}

	/// <summary>
	/// Return to authentication screen and display error message.
	/// </summary>
	/// <param name="status">The HTTP status code.</param>
	/// <param name="reasonCode">The error reason code</param>
	/// <param name="jsonError">The error JSON string.</param>
	/// <param name="cbObject">The user supplied callback object.</param>
	private void OnAuthenticationFailed(int status, int reasonCode, string jsonError, object cbObject)
	{
		GD.Print(string.Format("Authentication Failed:\n{0}  {1}  {2}", status, reasonCode, jsonError));

		_username = null;

		GoToAuthenticationScreen();

		if (_authenticationScreen != null)
		{
			if (_reconnecting)
			{
				GD.Print("Reconnect failed:\nStored profileID: " + _brainCloudWrapper.GetStoredProfileId() + "\nStored anonymousID: " + _brainCloudWrapper.GetStoredAnonymousId());
			}
			else
			{
				_authenticationScreen.SetErrorMessage("Authentication failed.");
			}
		}

		// Something went wrong while attempting to reload the authentication screen. Restart the app.
		else
		{
			OnError("Unknown AuthenticationScreen Error . . .");
		}
	}

	/// <summary>
	/// Attempt to log out of brainCloud and return to the Authentication Screen.
	/// </summary>
	private void OnLogOutRequested()
	{
		LoadScene("Logging out . . . ");

		SuccessCallback OnLogOutSuccess = (response, cbObject) =>
		{
			GD.Print(string.Format("Log Out Success:\n{0}", response));

			GoToAuthenticationScreen();
		};

		FailureCallback OnLogOutFailed = (status, reasonCode, jsonError, cbObject) =>
		{
			GD.Print(string.Format("Log Out Failed:\n{0}  {1}  {2}", status, reasonCode, jsonError));

			GoToLobbySelectScreen();

			if(_lobbySelectScreen != null)
			{
				_lobbySelectScreen.SetErrorMessage("Failed to log out.");
			}
			else
			{
				OnError("Unknown LobbySelectScreen Error while trying to log out . . .");
			}
		};


		_brainCloudWrapper.Logout(true, OnLogOutSuccess, OnLogOutFailed);
	}

	/// <summary>
	/// Enable RTT, register lobby event callback, and find/create a lobby.
	/// </summary>
	/// <param name="lobbyType">String value of lobby type selected on lobby select screen. Used for matchmaking and to determine game mode.</param>
	/// <param name="protocol">Relay transport protocol selected on the lobby select screen ("WEBSOCKET", "TCP", or "UDP").</param>
	private void OnMatchMakingRequested(string lobbyType, string protocol, bool usePingData)
	{
		// Fresh matchmaking — a new Lobby, so this-lobby chat starts empty.
		_lobbyChatHistory.Clear();

		_matchmakingStartMs = NowMs();
		_matchmakingCancelled = false;

		// Loading screen
		LoadScene("Enabling RTT . . .");

		// Remember the relay transport protocol to use once a room server is assigned.
		_relayConnectionType = protocol?.ToUpperInvariant() switch
		{
			"TCP" => RelayConnectionType.TCP,
			"UDP" => RelayConnectionType.UDP,
			_     => RelayConnectionType.WEBSOCKET
		};

		// Remember whether to gather region ping data before matchmaking.
		_usePingData = usePingData;

		// Game mode is derived from the (app-configured) lobby type name: anything containing
		// "Team" is team mode, otherwise free-for-all. Works for any AllLobbyTypes entry.
		_gameMode = lobbyType.Contains("Team", StringComparison.OrdinalIgnoreCase) ? "team" : "ffa";

		/* Enable RTT */

		Action OnEnableRTTSuccess = () =>
		{
			GD.Print("Enable RTT Success (or already enabled)");
			if (_matchmakingCancelled) return;

			LoadScene("Finding/Creating a " + lobbyType + " lobby . . .", cancellable: true);

			Dictionary<string, object> extraJSON = new Dictionary<string, object>
			{
				{ "colorIndex", _userColourIndex },
				{ "presentSinceStart", _userWasPresentSinceStart }
			};

			// Register Lobby callback
			_brainCloudWrapper.RTTService.RegisterRTTLobbyCallback(OnLobbyEventReceived);

			// Find or Create a Lobby
			_matchmaking = true;

			int rating = 0;
			int maxSteps = 1;
			Dictionary<string, object> algo = new Dictionary<string, object>
			{
				{ "strategy", "ranged-absolute" },
				{ "alignment", "center" },
				{ "ranges", new List<int>(){1000} }
			};
			Dictionary<string, object> filterJSON = new Dictionary<string, object>();
			int timeoutSecs = 0;
			bool isReady = false;
			string teamCode = _gameMode == "ffa" ? "all" : "";
			Dictionary<string, object> settings = new Dictionary<string, object>();
			string[] otherUserCxIDs = null;

			SuccessCallback OnFindOrCreateLobbySuccess = (jsonResponse, cbObject) =>
			{
				GD.Print(string.Format("FindOrCreateLobby Success:\n{0}", jsonResponse));
				if (_matchmakingCancelled) return;

				LoadScene("Joining lobby . . .", cancellable: true);
			};

			FailureCallback OnFindOrCreateLobbyFailed = (status, reasonCode, jsonError, cbObject) =>
			{
				GD.Print(string.Format("FindOrCreateLobby Failed:\n{0}  {1}  {2}", status, reasonCode, jsonError));

				OnMatchmakingFailed(status, reasonCode, jsonError, cbObject);
			};

			// Find/create the lobby. When ping data is enabled, gather region ping times
			// first so brainCloud can place us in the lowest-latency region.
			void DoFindLobby(bool withPingData)
			{
				if (withPingData)
				{
					_brainCloudWrapper.LobbyService.FindOrCreateLobbyWithPingData(lobbyType, rating, maxSteps, algo, filterJSON, isReady, extraJSON, teamCode, settings, otherUserCxIDs, OnFindOrCreateLobbySuccess, OnFindOrCreateLobbyFailed);
				}
				else
				{
					_brainCloudWrapper.LobbyService.FindOrCreateLobby(lobbyType, rating, maxSteps, algo, filterJSON, isReady, extraJSON, teamCode, settings, otherUserCxIDs, OnFindOrCreateLobbySuccess, OnFindOrCreateLobbyFailed);
				}
			}

			if (_usePingData)
			{
				LoadScene("Pinging regions . . .", cancellable: true);

				_brainCloudWrapper.LobbyService.GetRegionsForLobbies(
					new string[] { lobbyType },
					(regionsResponse, cbObj) =>
					{
						if (_matchmakingCancelled) return;
						_brainCloudWrapper.LobbyService.PingRegions(
							(pingResponse, _) =>
							{
								if (_matchmakingCancelled) return;
								// Capture our per-region ping times and share them with the lobby so
								// every member can show everyone's ping data (extra["pings"]).
								_pingData.Clear();
								var pingDataRaw = _brainCloudWrapper.LobbyService.PingData;
								if (pingDataRaw != null)
								{
									foreach (var kv in pingDataRaw) _pingData[kv.Key] = (int)kv.Value;
								}
								if (_pingData.Count > 0) extraJSON["pings"] = _pingData;
								DoFindLobby(true);
							},
							(status, reasonCode, jsonError, _) => { if (!_matchmakingCancelled) DoFindLobby(false); });
					},
					(status, reasonCode, jsonError, cbObj) => { if (!_matchmakingCancelled) DoFindLobby(false); });
			}
			else
			{
				DoFindLobby(false);
			}
		};

		// RTT always runs over WebSocket (the relay transport is what's selectable); goes through
		// EnsureRttEnabled since the lobby select screen usually already enabled RTT for global
		// chat, in which case this just resolves immediately.
		EnsureRttEnabled(OnEnableRTTSuccess, (status, reasonCode, jsonError) => OnMatchmakingFailed(status, reasonCode, jsonError, null));
	}

	/// <summary>
	/// Handle lobby events. 
	/// Update saved lobby, transition to lobby screen when matchmaking, and transition to match screen when connected to Relay Server.
	/// </summary>
	/// <param name="jsonResponse">JSON string received from the server containing lobby update data.</param>
	private void OnLobbyEventReceived(string jsonResponse)
	{
		GD.Print("Lobby event received");

		Dictionary<string, object> response = BrainCloud.JsonFx.Json.JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
		Dictionary<string, object> data = response["data"] as Dictionary<string, object>;

		// Update saved lobby
		if (data.ContainsKey("lobby"))
		{
			_lobby = (Dictionary<string, object>)data["lobby"];
			_lobbyID = (string)data["lobbyId"];

			// Update lobby/match members list and host button visibility (only the lobby owner / host can start and end matches)
			if (_lobbyScreen != null)
			{
				_lobbyScreen.UpdateLobbyMembers(_lobby);

				_lobbyScreen.ToggleStartButtonVisibility(IsEffectivelyHost());
				_lobbyScreen.SetReadyButtonState(_userIsReady);
			}
			if(_matchScreen != null)
			{
				// Seed defaults for any newly joined member before the HUD rebuilds its mask.
				EnsureSendMaskDefaults();
				_matchScreen.UpdateLobbyMembers(_lobby);
				RefreshActivePings(); // re-apply live pings to the rebuilt player rows
			}
			if (_matchSummaryScreen != null)
			{
				// Keep the "Queue for Rematch N/M" count live as other players ready up.
				_matchSummaryScreen.RefreshResults(_matchResult.Entries, _lobby, _userCxID, _userIsReady);
			}

			// There is now enough lobby data to display lobby screen (IF matchmaking was in progress)
			if (_matchmaking)
			{
				_matchmaking = false;

				GoToLobbyScreen();
			}
		}

		// Handle different lobby event operations
		if (response.ContainsKey("operation")){
			string operation = (string)response["operation"];

			switch(operation)
			{
				case "DISBANDED":
					if (data.ContainsKey("reason"))
					{
						Dictionary<string, object> reason = (Dictionary<string, object>)data["reason"];
						int code = (int)reason["code"];

						// Receiving "DISBANDED" for any reason other than "ROOM_READY" means we failed to launch the game
						if (code != ReasonCodes.RTT_ROOM_READY)
						{
							OnError("Lobby disbanded.");
						}
					}

					break;
				case "STARTING":

					// This user was "present" in the lobby while the game started
					_presentWhileStarted = true;

					// Prepare this user for match and send update to lobby
					_userWasPresentSinceStart = true;
					_userIsReady = true;

					Dictionary<string, object> extraJSON = new Dictionary<string, object>
					{
						{ "colorIndex", _userColourIndex },
						{ "presentSinceStart", _userWasPresentSinceStart }
					};

					_brainCloudWrapper.LobbyService.UpdateReady(_lobbyID, _userIsReady, extraJSON);

					// Stay on the Lobby screen (chat/etc. keep working) instead of jumping to a
					// blocking loading screen — SetProvisioning just drives a small inline status
					// line there. The actual screen change to Match only happens once relay truly
					// connects (OnRelayConnectSuccess -> GoToMatchScreen).
					_isProvisioning = true;
					_provisioningStatus = "Provisioning server...";
					_lobbyScreen?.SetProvisioning(true, _provisioningStatus);

					break;
				case "ROOM_ASSIGNED":

					_provisioningStatus = "Server assigned...";
					_lobbyScreen?.SetProvisioning(true, _provisioningStatus);

					break;
				case "ROOM_PROGRESS":

					int curStep = data.ContainsKey("curStep") ? Convert.ToInt32(data["curStep"]) : 0;
					int ofStep = data.ContainsKey("ofStep") ? Convert.ToInt32(data["ofStep"]) : 0;
					string progressMsg = data.ContainsKey("msg") ? (string)data["msg"] : "";
					_provisioningStatus = curStep > 0 ? $"{curStep}/{ofStep}: {progressMsg}" : progressMsg;
					_lobbyScreen?.SetProvisioning(true, _provisioningStatus);

					break;
				case "ROOM_READY":

					// Update saved server
					_server = data;

					_provisioningStatus = "Connecting...";
					_lobbyScreen?.SetProvisioning(true, _provisioningStatus);

					if (_presentWhileStarted)
					{
						ConnectToRelayServer();
					}
					else
					{
						if(_lobbyScreen != null)
						{
							_lobbyScreen.ToggleJoinButtonVisibility(true);
						}
					}

					break;
				case "SIGNAL":
					// This-lobby chat via the Lobby service's SendSignal. Trust the server's "from"
					// over whatever our own signalData claims, and skip echoes of our own signal
					// (compare by cxId, not name — two players could share a display name) since we
					// already appended it locally in OnLobbySignalSendRequested.
					if (data.ContainsKey("from") && data.ContainsKey("signalData"))
					{
						var from = (Dictionary<string, object>)data["from"];
						var signalData = (Dictionary<string, object>)data["signalData"];
						string fromCxId = from.ContainsKey("cxId") ? (string)from["cxId"] : "";
						string fromName = from.ContainsKey("name") ? (string)from["name"] : "Player";
						string text = signalData.ContainsKey("text") ? (string)signalData["text"] : "";

						if (!string.IsNullOrEmpty(text) && fromCxId != _userCxID)
						{
							_lobbyChatHistory.Add((fromName, text, false));
							_lobbyScreen?.AddLobbyChatMessage(fromName, text, false);
						}
					}
					break;
				default:
					// TODO:  are there any error conditions that should be handled here?
					break;
			}
		}
	}

	/// <summary>
	/// Return to lobby select screen and display error.
	/// </summary>
	/// <param name="status">The HTTYP status code.</param>
	/// <param name="reasonCode">The reason code.</param>
	/// <param name="jsonError">The error JSON string.</param>
	/// <param name="cbObject">User supplied callback object.</param>
	private void OnMatchmakingFailed(int status, int reasonCode, string jsonError, object cbObject)
	{
		GD.Print("MatchmakingFailed");
		if (jsonError != "DisableRTT Called")
		{
			GD.Print(string.Format("Matchmaking Failed:\n{0}  {1}  {2}", status, reasonCode, jsonError));

			GoToLobbySelectScreen();

			_brainCloudWrapper.RelayService.DeregisterRelayCallback();
			_brainCloudWrapper.RelayService.DeregisterSystemCallback();
			_brainCloudWrapper.RelayService.Disconnect();
			_brainCloudWrapper.RTTService.DeregisterAllRTTCallbacks();
			_brainCloudWrapper.RTTService.DisableRTT();

			if (_lobbySelectScreen != null)
			{
				_lobbySelectScreen.SetErrorMessage("Matchmaking failed.");
			}
			
			// Something went wrong while attempting to reload the lobby select screen. Restart the app.
			else
			{
				OnError("Unknown LobbySelectScreen Error . . .");
			}
		}
	}

	/// <summary>
	/// Handle messages related to match member mouse clicks/movement.
	/// </summary>
	/// <param name="netId">Net ID associated with the match member that triggered the event.</param>
	/// <param name="jsonResponse">Byte array containing event/message data.</param>
	private void OnRelayMessageReceived(short netId, byte[] jsonResponse)
	{
		var message = DeserializeString(jsonResponse);
		string operation = message.ContainsKey("op") ? (string)message["op"] : "";

		// Canvas-sync ops carry their own data and need no sender lookup (join-in-progress).
		switch (operation)
		{
			case "splotch_sync":
				HandleSplotchSync((Dictionary<string, object>)message["data"]);
				return;
			case "clear_splotches":
				ClearSplotches();
				return;
			case "match_result":
				HandleMatchResult((Dictionary<string, object>)message["data"]);
				return;
			case "lb_result":
				HandleLbResult((Dictionary<string, object>)message["data"]);
				return;
		}

		string memberCxID = _brainCloudWrapper.RelayService.GetCxIdForNetId(netId);

		// Live relay ping (no x/y payload) — record the sender's RTT for the HUD, then stop.
		if (operation == "relay_ping")
		{
			var pingData = (Dictionary<string, object>)message["data"];
			SetActivePing(memberCxID, Convert.ToInt32(pingData["ping"]));
			return;
		}

		Dictionary<string, object> matchMember = null;

		Dictionary<string, object>[] lobbyMembers = (Dictionary<string, object>[])_lobby["members"];
		foreach(Dictionary<string, object> lobbyMember in lobbyMembers)
		{
			string cxID = (string)lobbyMember["cxId"];
			if(cxID == memberCxID)
			{
				matchMember = lobbyMember;
				
				break;
			}
		}

		if(matchMember == null)
		{
			OnError("Relay / System Message error: member not in lobby");

			return;
		}

		Dictionary<string, object> extra = (Dictionary<string, object>)matchMember["extra"];

		// Get event coordinates
		Dictionary<string, object> data = (Dictionary<string, object>)message["data"];

		float xCoord = (float)Convert.ToDouble(data["x"]);
		float yCoord = (float)Convert.ToDouble(data["y"]);

		switch (operation)
		{
			// The user moved the mouse within the game area
			case "move":
				SetMemberPosition(matchMember, new Vector2(xCoord, yCoord));
				break;

			// The user clicked somewhere within the game area
			case "shockwave":
				int colourIndex = (int)extra["colorIndex"];
				// Use the sender's synced rotation (default to random if an older client omits it)
				float angle = data.ContainsKey("angle") ? (float)Convert.ToDouble(data["angle"]) : GD.Randf() * Mathf.Tau;
				CreateSplatter(new Vector2(xCoord, yCoord), colourIndex, angle);
				// Record in the persisted canvas so it can be replayed to JIP members. The
				// sender is already resolved via netId above — no wire field needed. Used for
				// per-player coverage attribution so two players sharing a colour don't merge.
				_splotches.Add(new SplotchRecord { X = xCoord, Y = yCoord, ColorIndex = colourIndex, Angle = angle, StartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), CxId = memberCxID });
				break;
			default:
				break;
		}
	}

	/// <summary>
	/// Apply a join-in-progress canvas sync received from the host: optionally clear the
	/// local canvas, then render + record each splotch in the batch.
	/// </summary>
	private void HandleSplotchSync(Dictionary<string, object> data)
	{
		bool first = data.ContainsKey("first") && Convert.ToBoolean(data["first"]);
		if (first) ClearSplotches();

		if (!data.ContainsKey("splotches") || data["splotches"] == null) return;

		foreach (object entry in (object[])data["splotches"])
		{
			if (entry is not Dictionary<string, object> sd) continue;

			float x = (float)Convert.ToDouble(sd["x"]);
			float y = (float)Convert.ToDouble(sd["y"]);
			int c = Convert.ToInt32(sd["c"]);
			float a = sd.ContainsKey("a") ? (float)Convert.ToDouble(sd["a"]) : GD.Randf() * Mathf.Tau;
			// Original creation time (epoch ms), carried so a re-sync preserves age and dotnet/
			// react/cpp receivers fade correctly — matches CPP. (The Splatter visual doesn't yet
			// consume this locally; that's the pending fade rework.)
			long t = sd.ContainsKey("t") ? Convert.ToInt64(sd["t"]) : 0;

			// "o" is a compact netId, resolved to a cxId now — the map that resolves it may
			// not survive past END_MATCH, so this must happen at receive time, never deferred.
			// Missing/unresolvable -> unattributed (coverage falls back to a colorIndex match).
			string ownerCxId = sd.ContainsKey("o") ? _brainCloudWrapper.RelayService.GetCxIdForNetId(Convert.ToInt16(sd["o"])) : null;

			CreateSplatter(new Vector2(x, y), c, a);
			_splotches.Add(new SplotchRecord { X = x, Y = y, ColorIndex = c, Angle = a, StartTimeMs = t, CxId = ownerCxId });
		}
	}

	/// <summary>
	/// Clear the persisted splotch canvas and remove all splatter visuals from the play area.
	/// </summary>
	private void ClearSplotches()
	{
		_splotches.Clear();

		var splatterMask = _cursorParty?.GetNodeOrNull("SplatterMask");
		if (splatterMask != null)
		{
			foreach (Node child in splatterMask.GetChildren())
			{
				child.QueueFree();
			}
		}
	}

	/// <summary>
	/// Send the full splotch canvas to the given player mask (join-in-progress re-sync).
	/// Chunked so each relay packet stays under MAX_PACKETSIZE; the first chunk carries
	/// "first":true so the receiver clears its canvas before appending.
	/// </summary>
	/// <param name="toMask">Relay player mask of the recipient(s).</param>
	private void SendSplotchSync(ulong toMask)
	{
		const int maxChunkBytes = 900; // headroom below relay MAX_PACKETSIZE (1024)
		bool isFirst = true;
		int i = 0;

		// Always send at least one packet (even when empty) so the receiver clears its canvas.
		do
		{
			var batch = new List<Dictionary<string, object>>();
			byte[] packet = null;

			while (i < _splotches.Count)
			{
				SplotchRecord s = _splotches[i];
				var entry = new Dictionary<string, object> { { "x", s.X }, { "y", s.Y }, { "c", s.ColorIndex }, { "a", s.Angle }, { "t", s.StartTimeMs } };
				// Compact netId, not the ~80-char cxId, to stay inside the chunk byte budget.
				// Resolved back to a cxId at receive time (see HandleSplotchSync) — never
				// deferred, since RelayComms clears its netId maps at END_MATCH.
				if (!string.IsNullOrEmpty(s.CxId))
				{
					short ownerNetId = _brainCloudWrapper.RelayService.GetNetIdForCxId(s.CxId);
					if (ownerNetId >= 0 && ownerNetId < BrainCloudRelay.MAX_PLAYERS) entry["o"] = ownerNetId;
				}
				batch.Add(entry);

				byte[] candidate = BuildSplotchSyncPacket(isFirst, batch);
				if (candidate.Length > maxChunkBytes && batch.Count > 1)
				{
					// This entry pushed the packet over the limit — back it out and flush.
					batch.RemoveAt(batch.Count - 1);
					break;
				}
				packet = candidate;
				i++;
			}

			packet ??= BuildSplotchSyncPacket(isFirst, batch);

			_brainCloudWrapper.RelayService.SendToPlayers(packet, toMask, true, true, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_2);
			isFirst = false;
		} while (i < _splotches.Count);
	}

	private byte[] BuildSplotchSyncPacket(bool isFirst, List<Dictionary<string, object>> batch)
	{
		Dictionary<string, object> json = new Dictionary<string, object>
		{
			{ "op", "splotch_sync" },
			{ "data", new Dictionary<string, object>
				{
					{ "first", isFirst },
					{ "splotches", batch.ToArray() }
				}
			}
		};
		return Encoding.ASCII.GetBytes(BrainCloud.JsonFx.Json.JsonWriter.Serialize(json));
	}

	/// <summary>
	/// Handle messages related to the match itself (i.e. members connecting/disconnecting).
	/// </summary>
	/// <param name="jsonResponse">JSON string containing event/message data.</param>
	private void OnSystemMessageReceived(string jsonResponse)
	{
		if (_cursorParty != null && _playing)
		{
			var json = BrainCloud.JsonFx.Json.JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);

			string op = (string)json["op"];
			string cxID = "";
			if (json.ContainsKey("cxId"))
			{
				cxID = (string)json["cxId"];
			}

			switch (op)
			{
				case "DISCONNECT":
					Member disconnectedMember = _matchMembers.SingleOrDefault(member => member.GetCxID() == cxID);

					if (disconnectedMember != null)
					{
						disconnectedMember.QueueFree();
						_matchMembers.Remove(disconnectedMember);
					}

					break;
				case "CONNECT":
					// The relay's CONNECT can beat the Lobby service's own MEMBER_JOIN/UPDATE
					// event to us, so _lobby["members"] may not have this peer yet. Seed a
					// placeholder (blank colour, no profileId) so the coverage board doesn't
					// just omit them — the next lobby event's rebuild overwrites it with real
					// data. Without this, a JIP joiner's own coverage board can render empty for
					// the whole match, and a player who disconnects right after connecting can
					// be dropped from the round's results entirely.
					if (!string.IsNullOrEmpty(cxID) && _lobby != null && _lobby.ContainsKey("members"))
					{
						var members = (Dictionary<string, object>[])_lobby["members"];
						bool alreadyKnown = members.Any(m => (string)m["cxId"] == cxID);
						if (!alreadyKnown)
						{
							var placeholder = new Dictionary<string, object>
							{
								{ "cxId", cxID },
								{ "name", "" },
								{ "extra", new Dictionary<string, object>() }
							};
							_lobby["members"] = members.Append(placeholder).ToArray();
						}
					}

					// Host re-syncs the splotch canvas to a join-in-progress member so they
					// see splotches created before they connected.
					if (IsEffectivelyHost() && !string.IsNullOrEmpty(cxID))
					{
						short newNetId = _brainCloudWrapper.RelayService.GetNetIdForCxId(cxID);
						if (newNetId >= 0 && newNetId < 64)
						{
							SendSplotchSync(1ul << newNetId);
						}
					}
					break;
				case "MIGRATE_OWNER":
					// Relay promoted a new host (the old one disconnected) — mirror that into
					// _lobby["ownerCxId"], the field every isHost check reads. The Lobby service's
					// own owner field catches up too via the next lobby-update event, but this
					// relay message gets there first. TickMatch re-checks IsEffectivelyHost every
					// tick, so the new host just picks up match duties (auto-end, match_result)
					// with no extra handoff needed. Skip this and nobody ever becomes host again
					// once the original leaves, and the match hangs forever.
					if (!string.IsNullOrEmpty(cxID) && _lobby != null)
					{
						_lobby["ownerCxId"] = cxID;
					}
					break;
				case "END_MATCH":
					// NOT _matchmaking = true here — that flag means "waiting for lobby data to
					// show the Lobby screen" (see OnLobbyEventReceived), and this UpdateReady call
					// a few lines down pushes a MEMBER_UPDATE lobby event back over RTT almost
					// immediately. If _matchmaking were true, that event would force GoToLobbyScreen()
					// and stomp the GoToMatchSummaryScreen() below before the player ever sees it.
					_playing = false;

					// Guard against a late-joined player, or a dropped/incomplete match_result chunk,
					// never reaching _matchResult before this system message arrives (it travels over
					// a separate pipe than the app-level match_result broadcast). Recompute locally
					// from splotches/lobby members we already have — same fallback TickMatch's
					// watchdog uses — before relay is torn down below, so the summary screen is never
					// stuck on "Waiting for results...".
					if (!(_matchResult.Valid && _matchResult.Round == _roundNumber))
					{
						var finalCoverage = Coverage.Compute(BuildSplotchLikes(), BuildMemberLikes());
						ApplyMatchResult(_roundNumber, ToMatchResultEntries(finalCoverage));
					}

					// Tear down the relay (mirrors CPP app.cpp / GDScript _on_match_ended). Without
					// this the relay stays connected and the next match stacks another set of
					// Register*Callback handlers on top of the live connection.
					_brainCloudWrapper.RelayService.DeregisterRelayCallback();
					_brainCloudWrapper.RelayService.DeregisterSystemCallback();
					_brainCloudWrapper.RelayService.Disconnect();

					_userIsReady = false;
					_userWasPresentSinceStart = false;

					// Match Summary + rematch-queue screen (BCLOUD-14489). _matchResult is usually
					// already populated by now — TickMatch's auto-end broadcasts it ResultGraceMs
					// before calling EndMatch. A manual early end skips that, so the screen just
					// shows "Waiting for results..." until it arrives.
					_awaitingRematch = true;
					_matchSummaryArrivalTimeMs = NowMs();

					// Actually clear readiness server-side too, not just the local mirror above —
					// otherwise the "Queue for Rematch N/M" count starts from whatever everyone's
					// pre-match ready state still was.
					if (!string.IsNullOrEmpty(_lobbyID))
					{
						Dictionary<string, object> readyExtra = new Dictionary<string, object>() { { "colorIndex", _userColourIndex } };
						if (_pingData.Count > 0) readyExtra["pings"] = _pingData;
						_brainCloudWrapper.LobbyService.UpdateReady(_lobbyID, false, readyExtra);
					}

					GoToMatchSummaryScreen();

					break;
				default:
					GD.Print("Default system msg: " + op);
					break;
			}
		}
	}

	/// <summary>
	/// Change this user's "colorIndex" value and send an update to other lobby members that this has occured.
	/// </summary>
	/// <param name="colourIndex">int value representing this user's new colour.</param>
	private void OnColourChanged(int colourIndex)
	{
		GD.Print("OnColourChanged");
		_userColourIndex = colourIndex;

		Dictionary<string, object> extraJSON = new Dictionary<string, object>
		{
			{ "colorIndex", colourIndex },
			{ "presentSinceStart", _userWasPresentSinceStart }
		};

		// Send update to lobby so that other members will be notified that this user has a new "colorIndex" value
		_brainCloudWrapper.LobbyService.UpdateReady(_lobbyID, _userIsReady, extraJSON);
	}

	/// <summary>
	/// Disconnect from Relay and RTT services, and return to the lobby select screen.
	/// </summary>
	private void OnLeaveLobbyRequested()
	{
		LoadScene("Disconnecting . . . ");

		// Without this, this player stays a "ghost" member of the lobby from every other
		// platform's point of view -- their isReady can never become true again, which
		// blocks the host's all-ready rematch fast path for everyone else until the server
		// eventually times the stale member out on its own.
		if (!string.IsNullOrEmpty(_lobbyID))
		{
			_brainCloudWrapper.LobbyService.LeaveLobby(_lobbyID);
		}

		_brainCloudWrapper.RelayService.DeregisterRelayCallback();
		_brainCloudWrapper.RelayService.DeregisterSystemCallback();
		_brainCloudWrapper.RelayService.Disconnect();
		_brainCloudWrapper.RTTService.DeregisterAllRTTCallbacks();
		_brainCloudWrapper.RTTService.DisableRTT();

		LoadScene("Returning to lobby select screen . . .");

		_lobby = new Dictionary<string, object>();
		_userIsReady = false;
		_userWasPresentSinceStart = false;
		_lobbyChatHistory.Clear();

		GoToLobbySelectScreen();
	}

	/// <summary>
	/// Sends a this-lobby chat message via the Lobby service's SendSignal. Appends locally
	/// right away — the SIGNAL case in OnLobbyEventReceived skips the echo of our own signal,
	/// which the server does send back to us too.
	/// </summary>
	private void OnLobbySignalSendRequested(string text)
	{
		if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(_lobbyID)) return;

		var signalData = new Dictionary<string, object> { { "text", text } };
		_brainCloudWrapper.LobbyService.SendSignal(_lobbyID, signalData);

		_lobbyChatHistory.Add((_username, text, true));
		_lobbyScreen?.AddLobbyChatMessage(_username, text, true);
	}

	/// <summary>
	/// Only availeble if this user was not in the lobby when a match started, and that match is still in progress. Connect to a match that has already started and is currently in progress.
	/// </summary>
	private void OnJoinMatchRequested()
	{
		_userIsReady = true;

		Dictionary<string, object> extraJSON = new Dictionary<string, object>
		{
			{ "colorIndex", _userColourIndex },
			{ "presentSinceStart", _userWasPresentSinceStart }
		};

		SuccessCallback OnJoinReady = (response, cbObject) =>
		{
			ConnectToRelayServer();
		};

		_brainCloudWrapper.LobbyService.UpdateReady(_lobbyID, _userIsReady, extraJSON, OnJoinReady);
	}

	/// <summary>
	/// Only available if this user is the lobby owner / host. Update ready status of this user, indicating that the match is ready to start.
	/// </summary>
	private void OnStartMatchRequested()
	{
		_userIsReady = true;
		_awaitingRematch = false; // in case this was triggered by TickRematchGate

		Dictionary<string, object> extraJSON = new Dictionary<string, object>
		{
			{ "colorIndex", _userColourIndex },
			{ "presentSinceStart", _userWasPresentSinceStart }
		};

		_brainCloudWrapper.LobbyService.UpdateReady(_lobbyID, _userIsReady, extraJSON);
	}

	/// <summary>
	/// Only the host has a "Start" button (starting the round is host-only), but every other
	/// member still needs a way to signal they're ready before the host starts — this toggles
	/// this player's own ready state.
	/// </summary>
	private void OnReadyToggleRequested()
	{
		_userIsReady = !_userIsReady;
		_lobbyScreen.SetReadyButtonState(_userIsReady);

		Dictionary<string, object> extraJSON = new Dictionary<string, object>
		{
			{ "colorIndex", _userColourIndex },
			{ "presentSinceStart", _userWasPresentSinceStart }
		};

		_brainCloudWrapper.LobbyService.UpdateReady(_lobbyID, _userIsReady, extraJSON);
	}

	/// <summary>
	/// Triggered when the local user (this user) moves the mouse within the game area. Update this member's position and send update to ALL other members.
	/// </summary>
	/// <param name="pos">Vector2 mouse movement position.</param>
	private void OnUserMoved(Vector2 pos)
	{
		// Send packet to all match members to update movement
		Dictionary<string, object> data = new Dictionary<string, object>()
		{
			{ "x", pos.X },
			{ "y", pos.Y }
		};

		Dictionary<string, object> json = new Dictionary<string, object>()
		{
			{ "op", "move" },
			{ "data", data }
		};

		string jsonString = BrainCloud.JsonFx.Json.JsonWriter.Serialize(json);

		byte[] jsonBytes = { 0x0 };
		jsonBytes = Encoding.ASCII.GetBytes(jsonString);

		// Movement goes to every player, with the reliability/order/channel chosen in the
		// Settings HUD (default unreliable — exact position isn't critical, loss is acceptable).
		_brainCloudWrapper.RelayService.SendToAll(jsonBytes, _sendReliable, _sendOrdered, _sendChannel);
	}

	/// <summary>
	/// Triggered when the local user (this user) clicks within he game area. Create a splatter at this position and send update to ALL other members.
	/// </summary>
	/// <param name="pos">Vector2 mouse click position.</param>
	/// <param name="button">MouseButton value indicating which type of splatter should be created.</param>
	private void OnUserClicked(Vector2 pos, MouseButton button)
	{
		// Pick a random rotation once and send it so every client renders this splotch
		// at the same orientation (wire field "angle", matching the GDScript RTA).
		float angle = GD.Randf() * Mathf.Tau;

		// Create / Send the splatter to match members
		Dictionary<string, object> data = new Dictionary<string, object>()
		{
			{ "x", pos.X },
			{ "y", pos.Y },
			{ "angle", angle }
		};

		Dictionary<string, object> json = new Dictionary<string, object>()
		{
			{ "op", "shockwave" },
			{ "data", data }
		};

		// TODO:  modify splatter colour, etc. based on MouseButton when Team Mode is implemented

		string jsonString = BrainCloud.JsonFx.Json.JsonWriter.Serialize(json);

		byte[] jsonBytes = { 0x0 };
		jsonBytes = Encoding.ASCII.GetBytes(jsonString);

		// Shockwaves are always reliable + unordered (the action must be guaranteed), but go only
		// to the players selected in the Settings HUD mask, on the chosen channel.
		_brainCloudWrapper.RelayService.SendToPlayers(jsonBytes, BuildSendMask(), true, false, _sendChannel);

		// Create the splatter locally and record it in the persisted canvas (for JIP replay) —
		// always a self-paint, so CxId is always our own (coverage attribution needs the real
		// player, not just colour — two players can share a colourIndex).
		CreateSplatter(pos, _userColourIndex, angle);
		_splotches.Add(new SplotchRecord { X = pos.X, Y = pos.Y, ColorIndex = _userColourIndex, Angle = angle, StartTimeMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(), CxId = _userCxID });
	}

	/// <summary>
	/// Build the relay player mask from the per-member allowSendTo toggles (shockwave targeting).
	/// A bit is set for each lobby member the user has left checked in the Settings HUD.
	/// </summary>
	private ulong BuildSendMask()
	{
		ulong mask = 0;
		if (_lobby == null) return mask;
		foreach (var member in (Dictionary<string, object>[])_lobby["members"])
		{
			string cxId = (string)member["cxId"];
			if (!_allowSendTo.TryGetValue(cxId, out bool allow) || !allow) continue;
			short netId = _brainCloudWrapper.RelayService.GetNetIdForCxId(cxId);
			if (netId >= 0 && netId < BrainCloudRelay.MAX_PLAYERS) mask |= 1ul << netId;
		}
		return mask;
	}

	/// <summary>
	/// Seed the send mask for any member missing an entry: everyone is a target by default
	/// except ourselves (we never receive our own relay messages). Preserves existing toggles.
	/// </summary>
	private void EnsureSendMaskDefaults()
	{
		if (_lobby == null) return;
		foreach (var member in (Dictionary<string, object>[])_lobby["members"])
		{
			string cxId = (string)member["cxId"];
			if (!_allowSendTo.ContainsKey(cxId)) _allowSendTo[cxId] = cxId != _userCxID;
		}
	}

	/// <summary>
	/// Broadcast our current relay-server RTT to all players (op "relay_ping"), and update our own
	/// HUD row immediately. Sent unreliable/unordered every 2s — a dropped ping just updates later.
	/// </summary>
	private void BroadcastRelayPing()
	{
		if (_brainCloudWrapper?.RelayService == null || !_brainCloudWrapper.RelayService.IsConnected()) return;

		// LastPing is in 100ns ticks; ×0.0001 → ms (see the .NET SDK ping-units note).
		int ping = (int)(_brainCloudWrapper.RelayService.LastPing * 0.0001f);
		SetActivePing(_userCxID, ping);

		Dictionary<string, object> json = new Dictionary<string, object>()
		{
			{ "op", "relay_ping" },
			{ "data", new Dictionary<string, object>() { { "ping", ping } } }
		};
		byte[] bytes = Encoding.ASCII.GetBytes(BrainCloud.JsonFx.Json.JsonWriter.Serialize(json));
		_brainCloudWrapper.RelayService.SendToAll(bytes, false, false, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1);
	}

	// Record a member's live relay RTT and push the formatted text to the HUD.
	private void SetActivePing(string cxId, int ping)
	{
		_activePing[cxId] = ping;
		_matchScreen?.SetActivePing(cxId, FormatPing(ping));
	}

	// Re-push all known live pings to the HUD (after it rebuilds its player rows).
	private void RefreshActivePings()
	{
		if (_matchScreen == null) return;
		foreach (var kv in _activePing) _matchScreen.SetActivePing(kv.Key, FormatPing(kv.Value));
	}

	private static string FormatPing(int ping)
	{
		if (ping < 0) return "...";
		if (ping >= 999) return "T/O";
		return ping + " ms";
	}

	private static long NowMs() => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

	// Mask of every OTHER lobby member (excludes self) — used to broadcast match_result/lb_result.
	private ulong BuildAllOthersMask()
	{
		ulong mask = 0;
		if (_lobby == null) return mask;
		foreach (var member in (Dictionary<string, object>[])_lobby["members"])
		{
			string cxId = (string)member["cxId"];
			if (cxId == _userCxID) continue;
			short netId = _brainCloudWrapper.RelayService.GetNetIdForCxId(cxId);
			if (netId >= 0 && netId < BrainCloudRelay.MAX_PLAYERS) mask |= 1ul << netId;
		}
		return mask;
	}

	private List<Coverage.SplotchLike> BuildSplotchLikes()
	{
		var list = new List<Coverage.SplotchLike>(_splotches.Count);
		foreach (var s in _splotches) list.Add(new Coverage.SplotchLike(s.X, s.Y, s.ColorIndex, s.CxId));
		return list;
	}

	private List<Coverage.MemberLike> BuildMemberLikes()
	{
		var list = new List<Coverage.MemberLike>();
		if (_lobby == null || !_lobby.ContainsKey("members")) return list;
		foreach (var member in (Dictionary<string, object>[])_lobby["members"])
		{
			string cxId = (string)member["cxId"];
			var extra = (Dictionary<string, object>)member["extra"];
			int colourIndex = extra.ContainsKey("colorIndex") ? Convert.ToInt32(extra["colorIndex"]) : 0;
			list.Add(new Coverage.MemberLike(cxId, colourIndex));
		}
		return list;
	}

	private static List<MatchResultEntry> ToMatchResultEntries(List<Coverage.Entry> coverage)
	{
		var list = new List<MatchResultEntry>(coverage.Count);
		foreach (var c in coverage)
			list.Add(new MatchResultEntry { CxId = c.CxId, Rank = c.Rank, CoveragePct = c.CoveragePct, Beaten = c.Beaten });
		return list;
	}

	/// <summary>
	/// Recomputes live coverage for the scoreboard sidebar, and drives the host's auto-end
	/// sequence at MatchDurationMs: compute + broadcast match_result, wait ResultGraceMs, then
	/// call the same EndMatch the "End Match" button uses. Ticked every CoverageRecomputeSec
	/// while _playing.
	/// </summary>
	private void TickMatch()
	{
		if (_matchStartTimeMs == 0) return;
		long nowMs = NowMs();

		var fresh = Coverage.Compute(BuildSplotchLikes(), BuildMemberLikes());
		foreach (var e in fresh)
		{
			var old = _coverage.Find(o => o.CxId == e.CxId);
			int prevRank = old != null ? old.Rank : e.Rank;
			long prevChangedAt = old != null ? old.RankChangedAtMs : 0;
			e.PrevRank = prevRank;
			e.RankChangedAtMs = (prevRank != e.Rank) ? nowMs : prevChangedAt;
		}
		_coverage = fresh;
		_matchScreen?.UpdateScoreboard(_coverage, _userCxID);

		bool isHost = IsEffectivelyHost();
		long elapsedMs = nowMs - _matchStartTimeMs;
		_matchScreen?.UpdateTimer(Math.Max(0, MatchDurationMs - elapsedMs));

		if (_matchPhase == MatchPhase.Running && elapsedMs >= MatchDurationMs && isHost)
		{
			if (!(_matchResult.Valid && _matchResult.Round == _roundNumber))
			{
				var finalCoverage = Coverage.Compute(BuildSplotchLikes(), BuildMemberLikes());
				ApplyMatchResult(_roundNumber, ToMatchResultEntries(finalCoverage));
				SendMatchResult(BuildAllOthersMask(), _roundNumber, finalCoverage);
			}
			_matchPhase = MatchPhase.ResultsBroadcast;
			_resultsSentAtMs = nowMs;
		}
		else if (_matchPhase == MatchPhase.ResultsBroadcast && isHost && nowMs - _resultsSentAtMs >= ResultGraceMs)
		{
			OnEndMatchRequested();
			_matchPhase = MatchPhase.Ended;
		}

		// Watchdog: well past the deadline with no authoritative result at all (a dropped
		// broadcast, or a gap during host migration) — compute and apply locally so the round
		// can't hang forever.
		if (!_matchResult.Valid && elapsedMs >= MatchDurationMs + ResultGraceMs + 3000)
		{
			var finalCoverage = Coverage.Compute(BuildSplotchLikes(), BuildMemberLikes());
			ApplyMatchResult(_roundNumber, ToMatchResultEntries(finalCoverage));
			if (isHost && _matchPhase != MatchPhase.Ended)
			{
				SendMatchResult(BuildAllOthersMask(), _roundNumber, finalCoverage);
				_matchPhase = MatchPhase.ResultsBroadcast;
				_resultsSentAtMs = nowMs;
			}
		}
	}

	/// <summary>
	/// Broadcasts the host-authoritative match result to the rest of the match, chunked to stay
	/// under a relay packet's byte budget (mirrors SendSplotchSync's pattern).
	/// </summary>
	private void SendMatchResult(ulong mask, int round, List<Coverage.Entry> coverage)
	{
		if (coverage.Count == 0 || mask == 0) return;

		const int maxBytes = 900;
		const int envelope = 80;
		var batches = new List<List<Dictionary<string, object>>>();
		var batch = new List<Dictionary<string, object>>();
		int currentSize = envelope;

		foreach (var c in coverage)
		{
			var entry = new Dictionary<string, object> { { "cx", c.CxId }, { "r", c.Rank }, { "c", (int)(c.CoveragePct * 100 + 0.5f) }, { "b", c.Beaten } };
			int entrySize = BrainCloud.JsonFx.Json.JsonWriter.Serialize(entry).Length + 1;
			if (currentSize + entrySize > maxBytes && batch.Count > 0)
			{
				batches.Add(batch);
				batch = new List<Dictionary<string, object>>();
				currentSize = envelope;
			}
			batch.Add(entry);
			currentSize += entrySize;
		}
		batches.Add(batch);

		for (int i = 0; i < batches.Count; i++)
		{
			var json = new Dictionary<string, object>
			{
				{ "op", "match_result" },
				{ "data", new Dictionary<string, object> { { "round", round }, { "first", i == 0 }, { "last", i == batches.Count - 1 }, { "e", batches[i].ToArray() } } }
			};
			byte[] bytes = Encoding.ASCII.GetBytes(BrainCloud.JsonFx.Json.JsonWriter.Serialize(json));
			_brainCloudWrapper.RelayService.SendToPlayers(bytes, mask, true, true, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1);
		}
	}

	private void HandleMatchResult(Dictionary<string, object> data)
	{
		int round = Convert.ToInt32(data["round"]);
		if (_matchResult.Valid && _matchResult.Round == round) return;

		bool first = data.ContainsKey("first") && Convert.ToBoolean(data["first"]);
		if (first) _pendingMatchResult = new List<MatchResultEntry>();

		if (data.ContainsKey("e") && data["e"] is object[] entries)
		{
			foreach (var o in entries)
			{
				if (o is not Dictionary<string, object> entry) continue;
				_pendingMatchResult.Add(new MatchResultEntry
				{
					CxId = (string)entry["cx"],
					Rank = Convert.ToInt32(entry["r"]),
					CoveragePct = Convert.ToInt32(entry["c"]) / 100f,
					Beaten = Convert.ToInt32(entry["b"])
				});
			}
		}

		bool last = data.ContainsKey("last") && Convert.ToBoolean(data["last"]);
		if (last)
		{
			ApplyMatchResult(round, _pendingMatchResult);
			_pendingMatchResult = new List<MatchResultEntry>();
		}
	}

	private void HandleLbResult(Dictionary<string, object> data)
	{
		int round = Convert.ToInt32(data["round"]);
		// lb_result almost always arrives after match_result is already applied (it depends on an
		// async cloud-script call), so a round guard here just drops it every time — that's why
		// summary cards used to get stuck on "Updating leaderboards...". No guard needed; stale/
		// duplicate chunks are already handled below via cxId matching.

		bool first = data.ContainsKey("first") && Convert.ToBoolean(data["first"]);
		if (first) _pendingLbChunk.Clear();

		if (data.ContainsKey("e") && data["e"] is object[] entries)
		{
			foreach (var o in entries)
			{
				if (o is not Dictionary<string, object> entry) continue;
				var delta = new LeaderboardDelta { Ready = true };

				void ReadPeriod(string key, LeaderboardPeriodDelta target)
				{
					if (!entry.ContainsKey(key)) return; // absent == "no change" for that period
					var j = (Dictionary<string, object>)entry[key];
					target.Improved = true;
					target.RankBefore = Convert.ToInt32(j["b"]);
					target.RankAfter = Convert.ToInt32(j["a"]);
				}
				ReadPeriod("pl", delta.PointsLifetime);
				ReadPeriod("pq", delta.PointsQuarterly);
				ReadPeriod("cl", delta.CoverageLifetime);
				ReadPeriod("cq", delta.CoverageQuarterly);
				_pendingLbChunk.Add(((string)entry["cx"], delta));
			}
		}

		bool last = data.ContainsKey("last") && Convert.ToBoolean(data["last"]);
		if (last)
		{
			foreach (var (cxId, delta) in _pendingLbChunk)
			{
				var entry = _matchResult.Entries.Find(e => e.CxId == cxId);
				if (entry != null) entry.LbDelta = delta;
				else _pendingLbResults[cxId] = delta;
			}
			_pendingLbChunk.Clear();
		}
	}

	/// <summary>
	/// Applies an authoritative coverage snapshot for a round — either computed locally (host,
	/// or the watchdog fallback) or reassembled from a "match_result" broadcast. Idempotent per
	/// round. Only the host posts to the cloud; everyone else just waits for the "lb_result"
	/// broadcast that produces.
	/// </summary>
	private void ApplyMatchResult(int round, List<MatchResultEntry> entries)
	{
		if (_matchResult.Valid && _matchResult.Round == round) return;
		_matchResult = new MatchResultData { Valid = true, Round = round, Entries = entries };

		// Drain any "lb_result" broadcasts that arrived before this round's match_result did.
		foreach (var e in entries)
		{
			if (_pendingLbResults.TryGetValue(e.CxId, out var pending))
			{
				e.LbDelta = pending;
				_pendingLbResults.Remove(e.CxId);
			}
		}

		_matchSummaryScreen?.RefreshResults(_matchResult.Entries, _lobby, _userCxID, _userIsReady);

		if (_leaderboardPostedRound == round) return;
		_leaderboardPostedRound = round;

		bool isHost = IsEffectivelyHost();
		if (isHost) HostPostMatchResultsToCloud(round, entries);
	}

	/// <summary>
	/// Host-only: posts the whole round's results to all four leaderboards in one trusted
	/// server-side call (the PostMatchResults cloud script). This replaces clients posting
	/// directly — postScoreToLeaderboardOnBehalfOf is Cloud-Code-only, so individual clients
	/// can't hit these boards themselves anymore.
	/// </summary>
	private void HostPostMatchResultsToCloud(int round, List<MatchResultEntry> entries)
	{
		var membersByCxId = new Dictionary<string, Dictionary<string, object>>();
		foreach (var m in (Dictionary<string, object>[])_lobby["members"]) membersByCxId[(string)m["cxId"]] = m;

		var entriesArr = new List<Dictionary<string, object>>();
		foreach (var e in entries)
		{
			if (!membersByCxId.TryGetValue(e.CxId, out var member)) continue;
			string profileId = member.ContainsKey("profileId") ? member["profileId"] as string : null;
			if (string.IsNullOrEmpty(profileId)) continue; // can't post server-side without a profileId

			entriesArr.Add(new Dictionary<string, object>
			{
				{ "profileId", profileId },
				{ "name", (string)member["name"] },
				{ "points", e.Beaten + 1 },
				{ "coverageBasisPoints", (int)(e.CoveragePct * 100 + 0.5f) }
			});
		}

		var payload = new Dictionary<string, object>
		{
			{ "round", round },
			{ "pointsLeaderboardId", pointsLeaderboardId },
			{ "pointsLeaderboardIdQuarterly", pointsLeaderboardIdQuarterly },
			{ "coverageLeaderboardId", coverageLeaderboardId },
			{ "coverageLeaderboardIdQuarterly", coverageLeaderboardIdQuarterly },
			{ "entries", entriesArr.ToArray() }
		};
		string payloadStr = BrainCloud.JsonFx.Json.JsonWriter.Serialize(payload);

		_brainCloudWrapper.ScriptService.RunScript("PostMatchResults", payloadStr,
			(jsonResponse, cbObject) =>
			{
				var response = BrainCloud.JsonFx.Json.JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
				var data = response.ContainsKey("data") ? response["data"] as Dictionary<string, object> : null;
				// The script's own return value is nested under data.response (a sibling of
				// runTimeData/success), not data itself — data.results is always empty/missing.
				var scriptResponse = data != null && data.ContainsKey("response") ? data["response"] as Dictionary<string, object> : null;
				var results = scriptResponse != null && scriptResponse.ContainsKey("results") ? scriptResponse["results"] as object[] : null;
				ApplyLeaderboardResultsFromCloud(round, results);
			},
			(status, reasonCode, jsonError, cbObject) =>
			{
				GD.Print($"PostMatchResults script call failed for round {round}: {jsonError}");
			});
	}

	/// <summary>
	/// Applies the PostMatchResults response (keyed by profileId) onto _matchResult.Entries
	/// (keyed by cxId — resolved via lobby members) and broadcasts the result to the rest of
	/// the match. Host-only.
	/// </summary>
	private void ApplyLeaderboardResultsFromCloud(int round, object[] resultsArr)
	{
		if (!(_matchResult.Valid && _matchResult.Round == round)) return; // a newer round has already started
		if (resultsArr == null || _lobby == null) return;

		var profileIdToCxId = new Dictionary<string, string>();
		foreach (var m in (Dictionary<string, object>[])_lobby["members"])
		{
			var profileId = m.ContainsKey("profileId") ? m["profileId"] as string : null;
			if (!string.IsNullOrEmpty(profileId)) profileIdToCxId[profileId] = (string)m["cxId"];
		}

		LeaderboardPeriodDelta ReadPeriod(Dictionary<string, object> j)
		{
			return new LeaderboardPeriodDelta
			{
				RankBefore = j.ContainsKey("before") ? Convert.ToInt32(j["before"]) : -1,
				RankAfter = j.ContainsKey("after") ? Convert.ToInt32(j["after"]) : -1,
				Improved = j.ContainsKey("improved") && Convert.ToBoolean(j["improved"])
			};
		}

		foreach (var o in resultsArr)
		{
			if (o is not Dictionary<string, object> r) continue;
			string profileId = r.ContainsKey("profileId") ? r["profileId"] as string : null;
			if (string.IsNullOrEmpty(profileId) || !profileIdToCxId.TryGetValue(profileId, out var cxId)) continue;

			var delta = new LeaderboardDelta
			{
				Ready = true,
				PointsLifetime = ReadPeriod((Dictionary<string, object>)r["pointsLifetime"]),
				PointsQuarterly = ReadPeriod((Dictionary<string, object>)r["pointsQuarterly"]),
				CoverageLifetime = ReadPeriod((Dictionary<string, object>)r["coverageLifetime"]),
				CoverageQuarterly = ReadPeriod((Dictionary<string, object>)r["coverageQuarterly"])
			};

			var entry = _matchResult.Entries.Find(e => e.CxId == cxId);
			if (entry != null) entry.LbDelta = delta;
		}

		_matchSummaryScreen?.RefreshResults(_matchResult.Entries, _lobby, _userCxID, _userIsReady);

		SendLeaderboardResultsToMask(BuildAllOthersMask(), round, _matchResult.Entries);
	}

	/// <summary>
	/// Broadcasts the host's cloud-computed leaderboard results for the whole round to the
	/// rest of the match — chunked exactly like match_result. Only entries with LbDelta.Ready
	/// are included; a period sub-object is present only when it actually improved (absent ==
	/// "no change" on receipt).
	/// </summary>
	private void SendLeaderboardResultsToMask(ulong mask, int round, List<MatchResultEntry> entries)
	{
		if (mask == 0) return;

		const int maxBytes = 900;
		const int envelope = 80;
		var batches = new List<List<Dictionary<string, object>>>();
		var batch = new List<Dictionary<string, object>>();
		int currentSize = envelope;

		void PutPeriod(Dictionary<string, object> je, string key, LeaderboardPeriodDelta pd)
		{
			if (!pd.Improved) return;
			je[key] = new Dictionary<string, object> { { "b", pd.RankBefore }, { "a", pd.RankAfter } };
		}

		foreach (var e in entries)
		{
			if (!e.LbDelta.Ready) continue;
			var je = new Dictionary<string, object> { { "cx", e.CxId } };
			PutPeriod(je, "pl", e.LbDelta.PointsLifetime);
			PutPeriod(je, "pq", e.LbDelta.PointsQuarterly);
			PutPeriod(je, "cl", e.LbDelta.CoverageLifetime);
			PutPeriod(je, "cq", e.LbDelta.CoverageQuarterly);

			int entrySize = BrainCloud.JsonFx.Json.JsonWriter.Serialize(je).Length + 1;
			if (currentSize + entrySize > maxBytes && batch.Count > 0)
			{
				batches.Add(batch);
				batch = new List<Dictionary<string, object>>();
				currentSize = envelope;
			}
			batch.Add(je);
			currentSize += entrySize;
		}
		batches.Add(batch); // always at least one (possibly empty) so "last" still fires

		for (int i = 0; i < batches.Count; i++)
		{
			var json = new Dictionary<string, object>
			{
				{ "op", "lb_result" },
				{ "data", new Dictionary<string, object> { { "round", round }, { "first", i == 0 }, { "last", i == batches.Count - 1 }, { "e", batches[i].ToArray() } } }
			};
			byte[] bytes = Encoding.ASCII.GetBytes(BrainCloud.JsonFx.Json.JsonWriter.Serialize(json));
			_brainCloudWrapper.RelayService.SendToPlayers(bytes, mask, true, true, BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1);
		}
	}

	/// <summary>
	/// Marks this player queued for a rematch and takes them back to the Lobby screen — used
	/// both by the Match Summary screen's "Queue for Rematch" button and by its own
	/// per-player MatchSummaryRematchMs auto-timeout.
	/// </summary>
	private void OnSetRematchReady(bool ready)
	{
		_userIsReady = ready;
		if (ready) GoToLobbyScreen();

		Dictionary<string, object> extraJSON = new Dictionary<string, object> { { "colorIndex", _userColourIndex } };
		if (_pingData.Count > 0) extraJSON["pings"] = _pingData;
		_brainCloudWrapper.LobbyService.UpdateReady(_lobbyID, ready, extraJSON);
	}

	/// <summary>
	/// Host-only gate on starting the next round: waits until every lobby member has queued for
	/// a rematch (each auto-queues by MatchSummaryRematchMs at the latest — see
	/// MatchSummaryScreen), or until that same deadline just elapses anyway. Then starts the
	/// next round like the first one. Re-checks isHost every call, so a host migration mid-Match-
	/// Summary is picked up automatically. Ticked every frame while _awaitingRematch.
	/// </summary>
	private void TickRematchGate()
	{
		if (_lobby == null || !_lobby.ContainsKey("ownerCxId")) return;
		bool isHost = IsEffectivelyHost();
		if (!isHost) return;

		var members = (Dictionary<string, object>[])_lobby["members"];
		bool allReady = members.Length > 0;
		foreach (var m in members)
		{
			if (!(m.ContainsKey("isReady") && Convert.ToBoolean(m["isReady"]))) { allReady = false; break; }
		}

		long elapsedMs = NowMs() - _matchSummaryArrivalTimeMs;
		if (allReady || elapsedMs >= MatchSummaryRematchMs)
		{
			OnStartMatchRequested();
		}
	}

	// Settings HUD callbacks (wired from MatchScreen signals).
	private void OnSendReliableChanged(bool value) => _sendReliable = value;
	private void OnSendOrderedChanged(bool value)  => _sendOrdered  = value;
	private void OnSendChannelChanged(long value)  => _sendChannel  = (int)value;
	private void OnSendMaskChanged(string cxId, bool value) => _allowSendTo[cxId] = value;

	/// <summary>
	/// End match — broadcasts EndMatch over relay; every client (including this host, once its
	/// own END_MATCH system message arrives) transitions to the Match Summary screen from there.
	/// Deliberately does NOT call LoadScene() here: LoadScene's "Reset other Scenes" step sets
	/// _playing = false, and OnSystemMessageReceived (which contains the END_MATCH case that
	/// calls GoToMatchSummaryScreen()) early-returns unless _playing is true. Calling it here
	/// meant the host — the only one who reaches this function — set _playing false BEFORE its
	/// own echoed-back END_MATCH message arrived, so that message was silently dropped and the
	/// host got stuck forever on whatever screen was showing at the time. cpp/JS/GDScript show
	/// no interim loading screen here either — the game screen just stays up until the real
	/// END_MATCH round-trip swaps it for Match Summary.
	/// </summary>
	private void OnEndMatchRequested()
	{
		Dictionary<string, object> extraJSON = new Dictionary<string, object>()
		{
			{ "cxId", _brainCloudWrapper.Client.RTTConnectionID },
			{ "lobbyId", _lobbyID },
			{ "op", "END_MATCH" }
		};

		_brainCloudWrapper.RelayService.EndMatch(extraJSON);

		// NOT _matchmaking = true here — see the matching comment in the END_MATCH case of
		// OnLobbyEventReceived's system-message switch; it would race GoToMatchSummaryScreen().
		_userIsReady = false;
		_userWasPresentSinceStart = false;
	}

	/// <summary>
	/// Disconnect from RTT/Relay and return to LobbySelect Screen.
	/// </summary>
	private void OnLeaveMatchRequested()
	{
		OnLeaveLobbyRequested();
	}

	private void GetSplatterProperties()
	{
		string[] properties;

		// Read both spellings: the app's palette property is "Colours" (comma-separated hex),
		// which the C++ and GDScript clients read; "Colors" is accepted as a fallback.
		properties = new string[]{"Colours", "Colors"};
		_brainCloudWrapper.GlobalAppService.ReadSelectedProperties(properties, OnGetColoursCallback, null);

		properties = new string[] { "PaintLifespan" };
		_brainCloudWrapper.GlobalAppService.ReadSelectedProperties(properties, OnGetLifespanCallback, null);

		properties = new string[] { "AppearDuration", "DisappearDuration" };
		_brainCloudWrapper.GlobalAppService.ReadSelectedProperties(properties, OnGetAnimDurationsCallback, null);

		// Lobby types are app-configured (not hardcoded) — read them from the AllLobbyTypes
		// global property, same as the Java/C#/JS clients.
		properties = new string[] { "AllLobbyTypes" };
		_brainCloudWrapper.GlobalAppService.ReadSelectedProperties(properties, OnGetLobbyTypesCallback, null);

		// Leaderboard ids — optional global properties, so the boards can be created/renamed
		// in the portal with no client rebuild (matches cpp's readLeaderboardId). Falls back
		// to the compiled-in defaults above when a property is absent.
		properties = new string[] { "PointsLeaderboardId", "PointsLeaderboardIdQuarterly", "CoverageLeaderboardId", "CoverageLeaderboardIdQuarterly" };
		_brainCloudWrapper.GlobalAppService.ReadSelectedProperties(properties, OnGetLeaderboardIdsCallback, null);
	}

	/// <summary>
	/// Parse the "AllLobbyTypes" global property — a JSON object { "key": { "lobby": "TypeName" } } —
	/// into the lobby-type list and populate the lobby select screen if it is open.
	/// </summary>
	private void OnGetLobbyTypesCallback(string jsonResponse, object cbObject)
	{
		var response = JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
		var data = response["data"] as Dictionary<string, object>;
		if (data == null || !data.ContainsKey("AllLobbyTypes")) return;
		var property = data["AllLobbyTypes"] as Dictionary<string, object>;
		if (property == null || !property.ContainsKey("value")) return;

		var value = property["value"] as string;
		if (string.IsNullOrEmpty(value)) return;

		var lobbyMap = JsonReader.Deserialize<Dictionary<string, object>>(value);
		if (lobbyMap == null) return;

		var lobbies = new List<string>();
		foreach (var entry in lobbyMap.Values)
		{
			if (entry is Dictionary<string, object> entryDict && entryDict.ContainsKey("lobby"))
			{
				string name = entryDict["lobby"] as string;
				if (!string.IsNullOrEmpty(name)) lobbies.Add(name);
			}
		}

		if (lobbies.Count == 0) return;
		_appLobbies = lobbies;

		// If the lobby select screen is already open, refresh its dropdown.
		_lobbySelectScreen?.SetLobbyTypes(_appLobbies);
	}

	private void OnGetColoursCallback(string jsonResponse, object cbObject)
	{
		var response = JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
		var data = response["data"] as Dictionary<string, object>;
		if (data == null) return;

		// The palette property is "Colours" (comma-separated bare hex, e.g. "FF3333,FF8800") on
		// this app — the format the C++ and GDScript clients read. Accept "Colors" and a JSON-array
		// value too so cursor/splotch colours match regardless of how the app stores the property.
		// An undefined/empty value keeps the built-in 40-colour default (never index a fixed array).
		var property = (data.ContainsKey("Colours") ? data["Colours"]
					   : (data.ContainsKey("Colors") ? data["Colors"] : null)) as Dictionary<string, object>;
		if (property == null || !property.ContainsKey("value")) return;
		var value = (property["value"] as string)?.Trim();
		if (string.IsNullOrEmpty(value)) return;

		string[] hexValues = value.StartsWith("[")
			? JsonReader.Deserialize<string[]>(value)   // JSON array of hex strings
			: value.Split(',');                          // comma-separated hex
		if (hexValues == null || hexValues.Length == 0) return;

		var palette = new List<Color>();
		foreach (var raw in hexValues)
		{
			string hex = raw.Trim().TrimStart('#');
			if (hex.Length > 0) palette.Add(new Color(string.Concat("#", hex)));
		}
		if (palette.Count > 0) Colours = palette.ToArray();
	}

	private void OnGetLifespanCallback(string jsonResponse, object cbObject)
	{
		var response = JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
		var data = response["data"] as Dictionary<string, object>;
		if (data == null || !data.ContainsKey("PaintLifespan")) return;
		var property = data["PaintLifespan"] as Dictionary<string, object>;
		if (property == null || !property.ContainsKey("value")) return;
		float value = Convert.ToSingle(property["value"]);
		splatterLifespan = value;
	}

	private void OnGetAnimDurationsCallback(string jsonResponse, object cbObject)
	{
		var response = JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
		var data = response["data"] as Dictionary<string, object>;
		if (data == null) return;

		if (data.ContainsKey("AppearDuration") && data["AppearDuration"] is Dictionary<string, object> ap && ap.ContainsKey("value"))
		{
			splatterAppear = Convert.ToSingle(ap["value"]);
		}

		var property = data["DisappearDuration"] as Dictionary<string, object>;
		if (property == null || !property.ContainsKey("value")) return;
		float value = Convert.ToSingle(property["value"]);
		splatterDisappear = value;
	}

	private void OnGetLeaderboardIdsCallback(string jsonResponse, object cbObject)
	{
		var response = JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
		var data = response["data"] as Dictionary<string, object>;
		if (data == null) return;

		string ReadId(string propName, string fallback)
		{
			if (!data.ContainsKey(propName) || data[propName] is not Dictionary<string, object> prop || !prop.ContainsKey("value")) return fallback;
			string v = prop["value"] as string;
			return string.IsNullOrEmpty(v) ? fallback : v;
		}

		pointsLeaderboardId = ReadId("PointsLeaderboardId", pointsLeaderboardId);
		pointsLeaderboardIdQuarterly = ReadId("PointsLeaderboardIdQuarterly", pointsLeaderboardIdQuarterly);
		coverageLeaderboardId = ReadId("CoverageLeaderboardId", coverageLeaderboardId);
		coverageLeaderboardIdQuarterly = ReadId("CoverageLeaderboardIdQuarterly", coverageLeaderboardIdQuarterly);

		// Keep the leaderboard viewer in sync with whatever board we actually post to below.
		LeaderboardPanel.PointsLeaderboardId = pointsLeaderboardId;
		LeaderboardPanel.PointsLeaderboardIdQuarterly = pointsLeaderboardIdQuarterly;
		LeaderboardPanel.CoverageLeaderboardId = coverageLeaderboardId;
		LeaderboardPanel.CoverageLeaderboardIdQuarterly = coverageLeaderboardIdQuarterly;
	}
}
