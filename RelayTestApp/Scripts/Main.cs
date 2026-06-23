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
	// runtime from the "Colors" global property (see OnGetColoursCallback); this is the default.
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

	// Screens / Scenes
	private AuthenticationScreen _authenticationScreen;
	private LoadingScreen _loadingScreen;
	private ErrorScreen _errorScreen;
	private LobbySelectScreen _lobbySelectScreen;
	private LobbyScreen _lobbyScreen;
	private MatchScreen _matchScreen;
	private CursorParty _cursorParty;

	// Scene Data
	private MarginContainer _sceneContainer;
	private Node _currentScene;

	// Game Data
	private string _gameMode = null;
	private string _teamCode = null;
	private bool _presentWhileStarted = false;
	private bool _matchmaking = false;
	private bool _playing = false;
	private string _lobbyID = null;
	private Dictionary<string, object> _lobby = null;
	private Dictionary<string, object> _server = null;
	private List<Member> _matchMembers = new List<Member>();

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
	private class SplotchRecord { public float X; public float Y; public int ColorIndex; public float Angle; }
	private readonly List<SplotchRecord> _splotches = new List<SplotchRecord>();
	
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
	private void LoadScene(string loadingScreenMsg)
	{
		// Create new LoadingScreen
		var loadingScreenScene = GD.Load<PackedScene>("res://Scenes/LoadingScreen.tscn");
		_loadingScreen = (LoadingScreen)loadingScreenScene.Instantiate();

		// Transition to loading screen
		ChangeScene(_loadingScreen);

		// Display loading screen message
		_loadingScreen.SetLoadingMessage(loadingScreenMsg);

		// Reset other Scenes
		_authenticationScreen = null;
		_lobbySelectScreen = null;
		_lobbyScreen = null;
		_matchScreen = null;
		_playing = false;

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

		// Connect event listener(s)
		_lobbySelectScreen.Connect(LobbySelectScreen.SignalName.LogOutRequested, new Callable(this, MethodName.OnLogOutRequested));
		_lobbySelectScreen.Connect(LobbySelectScreen.SignalName.MatchMakingRequested, new Callable(this, MethodName.OnMatchMakingRequested));
	}

	/// <summary>
	/// Display the lobby screen.
	/// </summary>
	private void GoToLobbyScreen()
	{
		var lobbyScene = GD.Load<PackedScene>("res://Scenes/LobbyScreen.tscn");
		_lobbyScreen = (LobbyScreen)lobbyScene.Instantiate();

		// Transition to lobby scene
		ChangeScene(_lobbyScreen);

		// Give the screen our own ping data + cxId so our row shows immediately, even before
		// the lobby echoes our extra["pings"] back to us.
		_lobbyScreen.SetLocalPingData(_pingData, _userCxID);

		_lobbyScreen.UpdateLobbyMembers(_lobby);

		string lobbyOwnerCxID = (string)_lobby["ownerCxId"];

		_lobbyScreen.ToggleStartButtonVisibility(lobbyOwnerCxID == _userCxID);

		_matchMembers.Clear();

		// Connect even listener(s)
		_lobbyScreen.Connect(LobbyScreen.SignalName.ColourChanged, new Callable(this, MethodName.OnColourChanged));
		_lobbyScreen.Connect(LobbyScreen.SignalName.LeaveLobbyRequested, new Callable(this, MethodName.OnLeaveLobbyRequested));
		_lobbyScreen.Connect(LobbyScreen.SignalName.JoinMatchRequested, new Callable(this, MethodName.OnJoinMatchRequested));
		_lobbyScreen.Connect(LobbyScreen.SignalName.StartMatchRequested, new Callable(this, MethodName.OnStartMatchRequested));
	}

	/// <summary>
	/// Display the match screen.
	/// </summary>
	private void GoToMatchScreen()
	{
		var matchScene = GD.Load<PackedScene>("res://Scenes/MatchScreen.tscn");
		_matchScreen = (MatchScreen)matchScene.Instantiate();

		// Transittion to match scene
		ChangeScene(_matchScreen);

		// Seed the per-player send mask (everyone but us) and give the match HUD the state it
		// needs to render the Players mask + the send-option controls.
		EnsureSendMaskDefaults();
		_matchScreen.SetSendMaskState(_allowSendTo, _userCxID);
		_matchScreen.SetSendOptions(_sendReliable, _sendOrdered, _sendChannel);

		_matchScreen.UpdateLobbyMembers(_lobby);

		_cursorParty = GetNode<CursorParty>("ScreenContainer/MatchScreen/MatchContainer/GameSide/GameArea/CursorParty");
		_cursorParty.SetUserColourIndex(_userColourIndex);

		// Start each match with a fresh splotch canvas (the host replays it to JIP members).
		_splotches.Clear();

		// Reset live-ping state for the new match (rows show "..." until first broadcast).
		_activePing.Clear();
		_pingBroadcastAccum = 0;

		_playing = true;

		string lobbyOwnerCxID = (string)_lobby["ownerCxId"];

		_matchScreen.ToggleEndMatchButtonVisibility(lobbyOwnerCxID == _userCxID);

		// Connect event listener(s)
		_matchScreen.Connect(MatchScreen.SignalName.EndMatchRequested, new Callable(this, MethodName.OnEndMatchRequested));
		_matchScreen.Connect(MatchScreen.SignalName.LeaveMatchRequested, new Callable(this, MethodName.OnLeaveMatchRequested));
		_matchScreen.Connect(MatchScreen.SignalName.SendReliableChanged, new Callable(this, MethodName.OnSendReliableChanged));
		_matchScreen.Connect(MatchScreen.SignalName.SendOrderedChanged, new Callable(this, MethodName.OnSendOrderedChanged));
		_matchScreen.Connect(MatchScreen.SignalName.SendChannelChanged, new Callable(this, MethodName.OnSendChannelChanged));
		_matchScreen.Connect(MatchScreen.SignalName.SendMaskChanged, new Callable(this, MethodName.OnSendMaskChanged));

		_cursorParty.Connect(CursorParty.SignalName.MouseMoved, new Callable(this, MethodName.OnUserMoved));
		_cursorParty.Connect(CursorParty.SignalName.MouseClicked, new Callable(this, MethodName.OnUserClicked));
	}

	/// <summary>
	/// Connect to Relay Server using data received from "ROOM_READY" lobby event.
	/// </summary>
	private void ConnectToRelayServer()
	{
		LoadScene("Connect to Relay Server . . .");

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

		SuccessCallback OnEnableRTTSuccess = (jsonResponse, cbObject) =>
		{
			GD.Print(string.Format("Enable RTT Success:\n{0}", jsonResponse));

			LoadScene("Finding/Creating a " + lobbyType + " lobby . . .");

			Dictionary<string, object> extraJSON = new Dictionary<string, object>
			{
				{ "colorIndex", _userColourIndex },
				{ "presentSinceStart", _userWasPresentSinceStart }
			};

			_userCxID = _brainCloudWrapper.RTTService.getRTTConnectionID();

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

				LoadScene("Joining lobby . . .");
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
				LoadScene("Pinging regions . . .");

				_brainCloudWrapper.LobbyService.GetRegionsForLobbies(
					new string[] { lobbyType },
					(regionsResponse, cbObj) =>
					{
						_brainCloudWrapper.LobbyService.PingRegions(
							(pingResponse, _) =>
							{
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
							(status, reasonCode, jsonError, _) => DoFindLobby(false));
					},
					(status, reasonCode, jsonError, cbObj) => DoFindLobby(false));
			}
			else
			{
				DoFindLobby(false);
			}
		};

		// RTT runs over WebSocket (the default and only RTT connection type used by the
		// CursorParty examples); the relay transport is selectable separately.
		_brainCloudWrapper.RTTService.EnableRTT(OnEnableRTTSuccess, OnMatchmakingFailed);
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

				string lobbyOwnerCxID = (string)_lobby["ownerCxId"];

				_lobbyScreen.ToggleStartButtonVisibility(lobbyOwnerCxID == _userCxID);
			}
			if(_matchScreen != null)
			{
				// Seed defaults for any newly joined member before the HUD rebuilds its mask.
				EnsureSendMaskDefaults();
				_matchScreen.UpdateLobbyMembers(_lobby);
				RefreshActivePings(); // re-apply live pings to the rebuilt player rows

				string lobbyOwnerCxID = (string)_lobby["ownerCxId"];

				_matchScreen.ToggleEndMatchButtonVisibility(lobbyOwnerCxID == _userCxID);
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

					LoadScene("Starting match . . .");

					break;
				case "ROOM_READY":

					// Update saved server
					_server = data;

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
				// Record in the persisted canvas so it can be replayed to JIP members
				_splotches.Add(new SplotchRecord { X = xCoord, Y = yCoord, ColorIndex = colourIndex, Angle = angle });
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

			CreateSplatter(new Vector2(x, y), c, a);
			_splotches.Add(new SplotchRecord { X = x, Y = y, ColorIndex = c, Angle = a });
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
				batch.Add(new Dictionary<string, object> { { "x", s.X }, { "y", s.Y }, { "c", s.ColorIndex }, { "a", s.Angle } });

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
					// Host re-syncs the splotch canvas to a join-in-progress member so they
					// see splotches created before they connected.
					if (_lobby != null && (string)_lobby["ownerCxId"] == _userCxID && !string.IsNullOrEmpty(cxID))
					{
						short newNetId = _brainCloudWrapper.RelayService.GetNetIdForCxId(cxID);
						if (newNetId >= 0 && newNetId < 64)
						{
							SendSplotchSync(1ul << newNetId);
						}
					}
					break;
				case "END_MATCH":
					_matchmaking = true;

					LoadScene("Match ended. Returning to lobby . . .");

					_userIsReady = false;
					_userWasPresentSinceStart = false;

					//GoToLobbyScreen();

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

		_brainCloudWrapper.RelayService.DeregisterRelayCallback();
		_brainCloudWrapper.RelayService.DeregisterSystemCallback();
		_brainCloudWrapper.RelayService.Disconnect();
		_brainCloudWrapper.RTTService.DeregisterAllRTTCallbacks();
		_brainCloudWrapper.RTTService.DisableRTT();

		LoadScene("Returning to lobby select screen . . .");

		_lobby = new Dictionary<string, object>();
		_userIsReady = false;
		_userWasPresentSinceStart = false;

		GoToLobbySelectScreen();
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

		// Create the splatter locally and record it in the persisted canvas (for JIP replay)
		CreateSplatter(pos, _userColourIndex, angle);
		_splotches.Add(new SplotchRecord { X = pos.X, Y = pos.Y, ColorIndex = _userColourIndex, Angle = angle });
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

	// Settings HUD callbacks (wired from MatchScreen signals).
	private void OnSendReliableChanged(bool value) => _sendReliable = value;
	private void OnSendOrderedChanged(bool value)  => _sendOrdered  = value;
	private void OnSendChannelChanged(long value)  => _sendChannel  = (int)value;
	private void OnSendMaskChanged(string cxId, bool value) => _allowSendTo[cxId] = value;

	/// <summary>
	/// End match and return to the lobby.
	/// </summary>
	private void OnEndMatchRequested()
	{
		LoadScene("Ending match (returning to lobby). . .");

		Dictionary<string, object> extraJSON = new Dictionary<string, object>()
		{
			{ "cxId", _brainCloudWrapper.Client.RTTConnectionID },
			{ "lobbyId", _lobbyID },
			{ "op", "END_MATCH" }
		};

		_brainCloudWrapper.RelayService.EndMatch(extraJSON);

		_matchmaking = true;
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

		properties = new string[]{"Colors"};
		_brainCloudWrapper.GlobalAppService.ReadSelectedProperties(properties, OnGetColoursCallback, null);

		properties = new string[] { "PaintLifespan" };
		_brainCloudWrapper.GlobalAppService.ReadSelectedProperties(properties, OnGetLifespanCallback, null);

		properties = new string[] { "AppearDuration", "DisappearDuration" };
		_brainCloudWrapper.GlobalAppService.ReadSelectedProperties(properties, OnGetAnimDurationsCallback, null);

		// Lobby types are app-configured (not hardcoded) — read them from the AllLobbyTypes
		// global property, same as the Java/C#/JS clients.
		properties = new string[] { "AllLobbyTypes" };
		_brainCloudWrapper.GlobalAppService.ReadSelectedProperties(properties, OnGetLobbyTypesCallback, null);
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
		// "Colors" may be undefined on the app — fall back to the built-in 40-colour default.
		if (data == null || !data.ContainsKey("Colors")) return;
		var property = data["Colors"] as Dictionary<string, object>;
		if (property == null || !property.ContainsKey("value")) return;

		// The "Colors" global property is a JSON array of hex strings, e.g. ["#FF3333","#FF8800"]
		// (same format the Java/C#/JS/C++ clients read). Rebuild the palette sized to the array so
		// all 40 (or however many) colours are available — never index into a fixed-size array.
		var value = property["value"] as string;
		if (string.IsNullOrEmpty(value)) return;

		string[] hexValues = JsonReader.Deserialize<string[]>(value);
		if (hexValues == null || hexValues.Length == 0) return;

		var palette = new Color[hexValues.Length];
		for (int ii = 0; ii < hexValues.Length; ii++)
		{
			string hex = hexValues[ii].TrimStart('#');
			palette[ii] = new Color(string.Concat("#", hex));
		}
		Colours = palette;
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
}
