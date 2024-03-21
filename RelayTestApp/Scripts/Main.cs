using BrainCloud;
using BrainCloud.JsonFx.Json;
using BrainCloud.LitJson;
using Godot;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

public partial class Main : Node
{
	public static Color[] Colours =
	{
		new Color("#000000"),
		new Color("#55415f"),
		new Color("#646964"),
		new Color("#d77355"),
		new Color("#508cd7"),
		new Color("#64b964"),
		new Color("#e6c86e"),
		new Color("#dcf5ff")
	};

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
	

	// User Data
	private string _userProfileID = null;
	private string _userCxID = null;
	private string _username = null;
	private int _userColourIndex = 0;
	private bool _userIsReady = false;
	private bool _userWasPresentSinceStart = false;

	// brainCloud data
	private string _url = "https://api.braincloudservers.com/dispatcherv2";
	private string _appSecret = "";
	private string _appID = "";
	private string _version = "1.0.0";
	private BrainCloudWrapper _brainCloudWrapper;

	public override void _Ready()
	{
		_sceneContainer = GetNode<MarginContainer>("ScreenContainer");

		StartApp();
	}

	public override void _Process(double delta)
	{
		// Make sure you invoke this method in Update, or else you won't get any callbacks
		_brainCloudWrapper.RunCallbacks();
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
		_brainCloudWrapper.Init(_url, _appSecret, _appID, _version);

		if (!_brainCloudWrapper.Client.Initialized)
		{
			OnError("Failed to initialize brainCloud. Verify appID, app secret, and URL");
		}
		else
		{
			_brainCloudWrapper.Client.EnableLogging(true);

			GoToAuthenticationScreen();
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

        _matchScreen.UpdateLobbyMembers(_lobby);

		_cursorParty = GetNode<CursorParty>("ScreenContainer/MatchScreen/MatchContainer/GameSide/GameArea/CursorParty");
		_cursorParty.SetCustomCursor("Art/Cursors/arrow" + _userColourIndex + ".png");

		_playing = true;

        string lobbyOwnerCxID = (string)_lobby["ownerCxId"];

        _matchScreen.ToggleEndMatchButtonVisibility(lobbyOwnerCxID == _userCxID);

        // Connect event listener(s)
        _matchScreen.Connect(MatchScreen.SignalName.EndMatchRequested, new Callable(this, MethodName.OnEndMatchRequested));
		_matchScreen.Connect(MatchScreen.SignalName.LeaveMatchRequested, new Callable(this, MethodName.OnLeaveMatchRequested));

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

		// TODO:  only WebSocket is implemented right now
		// Set Relay Connect Options
		RelayConnectionType relayConnectionType = RelayConnectionType.WEBSOCKET;

		bool ssl = false;
		string host = (string)connectData["address"];
		int port = (int)ports["ws"];
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
	/// Create a Shockwave instance.
	/// </summary>
	/// <param name="pos"></param>
	/// <param name="colourIndex"></param>
	private void CreateShockwave(Vector2 pos, int colourIndex)
	{
		// Normalize Shockwave position coordinates for an accurate position across different platforms/resolutions/screens
		float xCoord = pos.X;
		float yCoord = pos.Y;
		xCoord *= 800;
		yCoord *= 600;

		// Create new Shockwave and add it to the list
		var shockwave = GD.Load<PackedScene>("res://Scenes/Shockwave.tscn");
		Shockwave newShockwave = (Shockwave)shockwave.Instantiate();
		_cursorParty.AddChild(newShockwave); 
		newShockwave.Position = (new Vector2(xCoord, yCoord));
		newShockwave.Modulate = Colours[colourIndex];
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

		// Update player name

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
		_brainCloudWrapper.PlayerStateService.UpdateName(_username, OnUpdateNameSuccess, OnUpdateNameFailed);
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
			_authenticationScreen.SetErrorMessage("Authentication failed.");
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
	private void OnMatchMakingRequested(string lobbyType)
	{

		// Loading screen
		LoadScene("Enabling RTT . . .");

		// Set game mode based on selected lobby type
		switch (lobbyType)
		{
			case "CursorPartyV2":
			case "CursorPartyV2Backfill":
				_gameMode = "ffa";
				break;
			case "TeamCursorPartyV2":
			case "TeamCursorPartyV2Backfill":
				_gameMode = "team";
				break;
			default:
				OnError("Invalid lobby type selected . . .");
				break;
		}

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

			_brainCloudWrapper.LobbyService.FindOrCreateLobby(lobbyType, rating, maxSteps, algo, filterJSON, timeoutSecs, isReady, extraJSON, teamCode, settings, otherUserCxIDs, OnFindOrCreateLobbySuccess, OnFindOrCreateLobbyFailed);
		};

		// TODO:  implement different RTTConnectionTypes
		_brainCloudWrapper.RTTService.EnableRTT(RTTConnectionType.WEBSOCKET, OnEnableRTTSuccess, OnMatchmakingFailed);
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
				_matchScreen.UpdateLobbyMembers(_lobby);

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
		string memberCxID = _brainCloudWrapper.RelayService.GetCxIdForNetId(netId);
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
		var json = DeserializeString(jsonResponse);
		Dictionary<string, object> data = (Dictionary<string, object>)json["data"];

		float xCoord = (float)Convert.ToDouble(data["x"]);
		float yCoord = (float)Convert.ToDouble(data["y"]);

		switch ((string)json["op"])
		{
			// The user moved the mouse within the game area
			case "move":
				SetMemberPosition(matchMember, new Vector2(xCoord, yCoord));
				break;
			
			// The user clicked somewhere within the game area
			case "shockwave":
				CreateShockwave(new Vector2(xCoord, yCoord), (int)extra["colorIndex"]);
				break;
			default:
				break;
		}
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
					// TODO:  update allowSendTo for all members
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

		// We send the movement update as unreliable. Exact position is not important and we can accept packet loss
		bool reliable = false;
		bool ordered = true;
		int channel = BrainCloudRelay.CHANNEL_HIGH_PRIORITY_1;

		_brainCloudWrapper.RelayService.SendToAll(jsonBytes, reliable, ordered, channel);
	}

	/// <summary>
	/// Triggered when the local user (this user) clicks within he game area. Create a shockwave at this position and send update to ALL other members.
	/// </summary>
	/// <param name="pos">Vector2 mouse click position.</param>
	/// <param name="button">MouseButton value indicating which type of shockwave should be created.</param>
	private void OnUserClicked(Vector2 pos, MouseButton button)
	{
		// Create / Send the shockwave to match members
		Dictionary<string, object> data = new Dictionary<string, object>()
		{
			{ "x", pos.X },
			{ "y", pos.Y }
		};

		Dictionary<string, object> json = new Dictionary<string, object>()
		{
			{ "op", "shockwave" },
			{ "data", data }
		};

		// TODO:  modify shockwave colour, etc. based on MouseButton when Team Mode is implemented

		string jsonString = BrainCloud.JsonFx.Json.JsonWriter.Serialize(json);

		byte[] jsonBytes = { 0x0 };
		jsonBytes = Encoding.ASCII.GetBytes(jsonString);

		// We send the shockewave event as reliable because such action needs to be guaranteed
		bool reliable = true;
		bool ordered = false;
		int channel = BrainCloudRelay.CHANNEL_HIGH_PRIORITY_2;

		_brainCloudWrapper.RelayService.SendToAll(jsonBytes, reliable, ordered, channel);

		// Create the shockwave locally
		CreateShockwave(pos, _userColourIndex);
	}

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
}
