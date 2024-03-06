using BrainCloud;
using BrainCloud.JsonFx.Json;
using Godot;
using Microsoft.VisualBasic;
using System;
using System.Collections.Generic;
using static GameManager;
using System.Diagnostics;
using System.Text;

public partial class BCManager : Node
{
    [Signal]
    public delegate void AuthenticationRequestSentEventHandler();
    [Signal]
	public delegate void AuthenticationRequestCompletedEventHandler();
	[Signal]
	public delegate void AuthenticationRequestFailedEventHandler();
    [Signal]
    public delegate void LogoutRequestSuccessEventHandler();
	[Signal]
	public delegate void FoundLobbyEventHandler();
	[Signal]
	public delegate void FailedToFindLobbyEventHandler();
	[Signal]
	public delegate void LobbyUpdatedEventHandler();
    [Signal]
    public delegate void MatchUpdatedEventHandler();
    [Signal]
    public delegate void CursorPartyUpdatedEventHandler();
    [Signal]
    public delegate void ConnectedToRelayEventHandler();
    [Signal]
    public delegate void FoundGameInProgressEventHandler();
    [Signal]
    public delegate void LeaveLobbyReadyEventHandler();
    [Signal]
    public delegate void MatchEndedEventHandler();
    [Signal]
    public delegate void StartingMatchEventHandler();

	private BrainCloudWrapper _brainCloud;
	private GameManager _gameManager;

	private bool _presentWhileStarted;

    // TODO: add you brainCloud app IDs
    private string url = "https://api.braincloudservers.com/dispatcherv2";
    private string appId = "";
    private string secretKey = "";
    private string version = "1.0.0";

    public override void _Ready()
	{
		_gameManager = GetNode<GameManager>("/root/GameManager");
		
		// Create and initialize the BrainCloud wrapper
		_brainCloud = new BrainCloudWrapper();

		_brainCloud.Init(url, secretKey, appId, version);

		_brainCloud.Client.EnableLogging(true);
	}

	public override void _Process(double delta)
	{
		// Make sure you invoke this method in Update, or else you won't get any callbacks
		_brainCloud.RunCallbacks();
	}

	public void AuthenticateUniversal(string universalID, string password)
	{
		BrainCloud.FailureCallback failureCallback = (status, reasonCode, jsonError, cbObject) =>
		{
			EmitSignal(SignalName.AuthenticationRequestFailed);
		};

        GameManager.Instance.CurrentUserInfo.Username = universalID;
		_brainCloud.AuthenticateUniversal(universalID, password, true, HandlePlayerState, failureCallback);
	}

    public void LogOut()
    {
        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Success | {0}", response));
            EmitSignal(SignalName.LogoutRequestSuccess);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloud.Logout(true, successCallback, failureCallback);
    }

	public void FindLobby(string lobbyType)
	{
		_brainCloud.RTTService.RegisterRTTLobbyCallback(OnLobbyEvent);

		SuccessCallback enableRTTSuccessCallback = (responseData, cbObject) =>
		{
			// algo: The algorithm to use for increasing the search scope
			Dictionary<string, object> algo = new Dictionary<string, object>();

			algo["strategy"] = "ranged-absolute";
			algo["alignment"] = "center";

			List<int> ranges = new List<int>
		{
			1000
		};

			algo["ranges"] = ranges;

			// filterJson: Used to help filter the list of rooms to consider. Passed to the matchmaking filter, if configured.
			Dictionary<string, object> filterJson = new Dictionary<string, object>();

			// extraJson: Initial extra-data about this user.
			Dictionary<string, object> extra = new Dictionary<string, object>();
			extra["colorIndex"] = (int)GameManager.Instance.CurrentUserInfo.UserGameColor;

			// teamCode:
			string teamCode = GameManager.Instance.Mode == GameManager.GameMode.FreeForAll ? "all" : "";

			// settings:
			Dictionary<string, object> settings = new Dictionary<string, object>();

			SuccessCallback successCallback = (response, cbObject) =>
			{
				GD.Print(string.Format("Lobby Found | {0}", response));
				
			};

			FailureCallback failureCallback = (status, code, error, cbObject) =>
			{
				GD.Print(string.Format("FindOrCreateLobby Failed | {0}  {1}  {2}", status, code, error));
			};

			_brainCloud.LobbyService.FindOrCreateLobby(lobbyType, 0, 1, algo, filterJson, 0, false, extra, teamCode, settings, null, successCallback, failureCallback);
		};

		FailureCallback enableRTTFailureCallback = (status, code, error, cbObject) =>
		{
			GD.Print(string.Format("EnableRTT Failed {0}  {1}  {2}", status, code, error));
		};

		// Real-time Tech (RTT) must be checked on the dashboard, under Design | Core App Info | Advanced Settings.
		_brainCloud.RTTService.EnableRTT(RTTConnectionType.WEBSOCKET, enableRTTSuccessCallback, OnRTTDisconnected);
	}

    public void LeaveGame()
    {
        _brainCloud.RelayService.DeregisterRelayCallback();
        _brainCloud.RelayService.DeregisterSystemCallback();
        _brainCloud.RelayService.Disconnect();
        _brainCloud.RTTService.DeregisterAllRTTCallbacks();
        _brainCloud.RTTService.DisableRTT();

        GameManager.Instance.CurrentLobby = null;
        GameManager.Instance.CurrentServer = null;
        GameManager.Instance.IsReady = false;
        GameManager.Instance.CurrentUserInfo.IsAlive = false;

        EmitSignal(SignalName.LeaveLobbyReady);
    }

    public void EndMatch()
    {
        EmitSignal(SignalName.LobbyUpdated);
        Dictionary<string, object> json = new Dictionary<string, object>();
        json["cxId"] = _brainCloud.Client.RTTConnectionID;
        json["lobbyId"] = GameManager.Instance.CurrentLobby.LobbyID;
        json["op"] = "END_MATCH";
        _brainCloud.RelayService.EndMatch(json);
    }

    public void JoinMatch()
    {
        ConnectRelay();
    }

	public void UpdateReady(Dictionary<string, object> extraJson)
	{
		_brainCloud.LobbyService.UpdateReady(GameManager.Instance.CurrentLobby.LobbyID, GameManager.Instance.IsReady, extraJson);
	}

    public void SendRelayMessage(Dictionary<string, object> in_dict)
    {
        string jsonData = JsonWriter.Serialize(in_dict);
        byte[] in_data = Encoding.ASCII.GetBytes(jsonData);
        ulong to_netID = BrainCloudRelay.TO_ALL_PLAYERS;
        bool in_reliable = true;
        bool in_ordered = false;
        int in_channel = 0;

        _brainCloud.RelayService.Send(in_data, to_netID, in_reliable, in_ordered, in_channel);
    }

    private void OnRTTDisconnected(int status, int reasonCode, string jsonError, object cbObject)
    {
        if (jsonError == "DisableRTT Called") return; // Ignore

        LeaveGame();
    }

    private void HandlePlayerState(string jsonResponse, object cobject)
	{
		var response = JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
		var data = response["data"] as Dictionary<string, object>;
		var tempUsername = GameManager.Instance.CurrentUserInfo.Username;
		var userInfo = GameManager.Instance.CurrentUserInfo;
		userInfo = new UserInfo();
		userInfo.ID = data["profileId"] as string;

		SuccessCallback successCallback = (response, cbObject) =>
		{
			GD.Print(string.Format("UpdateName Success | {0}", response));
			EmitSignal(SignalName.AuthenticationRequestCompleted);
		};
		FailureCallback failureCallback = (status, code, error, cbObject) =>
		{
			GD.Print(string.Format("UpdateName Failed | {0}  {1}  {2}", status, code, error));
		};

		// If no username is set for this user, ask for it
		if (!data.ContainsKey("playerName"))
		{
			// Update name for display
			_brainCloud.PlayerStateService.UpdateName(tempUsername, successCallback, failureCallback);
		}
		else
		{
			userInfo.Username = data["playerName"] as string;
			if (string.IsNullOrEmpty(userInfo.Username))
			{
				userInfo.Username = tempUsername;
			}
			_brainCloud.PlayerStateService.UpdateName(userInfo.Username, successCallback, failureCallback);
		}
		GameManager.Instance.CurrentUserInfo = userInfo;
	}

    // Connect to the Relay server and start the game
    private void ConnectRelay()
    {
        _presentWhileStarted = false;
        _brainCloud.RTTService.RegisterRTTLobbyCallback(OnLobbyEvent);
        _brainCloud.RelayService.RegisterRelayCallback(OnRelayMessage);
        _brainCloud.RelayService.RegisterSystemCallback(OnRelaySystemMessage);

        int port = 0;
        GameManager.Instance.Protocol = RelayConnectionType.WEBSOCKET; // TODO:  right now, only WebSocket is implemented
        switch (GameManager.Instance.Protocol)
        {
            case RelayConnectionType.WEBSOCKET:
                port = GameManager.Instance.CurrentServer.WsPort;
                break;
            case RelayConnectionType.TCP:
                port = GameManager.Instance.CurrentServer.TcpPort;
                break;
            case RelayConnectionType.UDP:
                port = GameManager.Instance.CurrentServer.UdpPort;
                break;
            default:
                break;
        }

        Server server = GameManager.Instance.CurrentServer;

        RelayConnectionType connectionType = GameManager.Instance.Protocol;
        RelayConnectOptions options = new RelayConnectOptions(false, server.Host, port, server.Passcode, server.LobbyId);

        SuccessCallback successCallback = (response, cbObject) =>
        {
            GD.Print(string.Format("Connect Success | {0}", response));
            EmitSignal(SignalName.ConnectedToRelay);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Connect Failed | {0}  {1}  {2}", status, code, error));
        };

        _brainCloud.RelayService.Connect(connectionType, options, successCallback, failureCallback);
    }

    private Dictionary<string, object> DeserializeString(byte[] in_data, char in_joinChar = '=', char in_splitChar = ';')
    {
        Dictionary<string, object> toDict = new Dictionary<string, object>();
        string jsonMessage = Encoding.ASCII.GetString(in_data);
        if (jsonMessage == "") return toDict;

        try
        {
            toDict = (Dictionary<string, object>)JsonReader.Deserialize(jsonMessage);
        }
        catch (Exception)
        {
            GD.Print("COULD NOT SERIALIZE " + jsonMessage);
        }
        return toDict;
    }

    private void OnLobbyEvent(string jsonResponse)
	{
        Dictionary<string, object> response = JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
        Dictionary<string, object> jsonData = response["data"] as Dictionary<string, object>;

		// If there is a lobby object present in the message, update our lobby
        // state with it.
        if (jsonData.ContainsKey("lobby"))
        {
            GameManager.Instance.CurrentLobby = new Lobby(jsonData["lobby"] as Dictionary<string, object>, jsonData["lobbyId"] as string);

            EmitSignal(SignalName.FoundLobby);
            EmitSignal(SignalName.LobbyUpdated);
            EmitSignal(SignalName.MatchUpdated);
        }

        // Using the key "operation" to determine what state the lobby is in
        if (response.ContainsKey("operation"))
        {
            var operation = response["operation"] as string;
            switch (operation)
            {
                case "DISBANDED":
                    {
                        var reason = jsonData["reason"] as Dictionary<string, object>;
                        if ((int)reason["code"] != ReasonCodes.RTT_ROOM_READY)
                        {
                            // Disbanded for any other reason than ROOM_READY, means we failed to launch the game.
                            LeaveGame();
                        }

                        break;
                    }
                case "STARTING":
                    _presentWhileStarted = true;
                    GameManager.Instance.UpdatePresentSinceStart();
                    EmitSignal(SignalName.StartingMatch);
                    
                    break;
                case "ROOM_READY":
                    GameManager.Instance.CurrentServer = new Server(jsonData);
                    
                    EmitSignal(SignalName.LobbyUpdated);
                    EmitSignal(SignalName.MatchUpdated);
                    EmitSignal(SignalName.CursorPartyUpdated);

                    // Check to see if a user joined the lobby before the match started or after.
                    // If a user joins while match is in progress, you will only receive MEMBER_JOIN & ROOM_READY RTT updates.
                    if (_presentWhileStarted)
                    {
                        ConnectRelay();
                    }
                    else
                    {
                        EmitSignal(SignalName.FoundGameInProgress);
                    }
                    break;
            }
        }
    }

	private void OnRelayMessage(short netId, byte[] jsonResponse)
	{
        var memberProfileId = _brainCloud.RelayService.GetProfileIdForNetId(netId);

        var json = DeserializeString(jsonResponse);
        Lobby lobby = GameManager.Instance.CurrentLobby;
        foreach (var member in lobby.Members)
        {
            if (member.ID == memberProfileId)
            {
                var data = json["data"] as Dictionary<string, object>;
                if (data == null)
                {
                    break;
                }
                var op = json["op"] as string;
                if (op == "move")
                {
                    member.IsAlive = true;
                    float mousePosX = (float)Convert.ToDouble(data["x"]);
                    float mousePosY = (float)Convert.ToDouble(data["y"]);

                    member.MousePosition.Y = mousePosY;
                    member.MousePosition.X = mousePosX;
                }
                else if (op == "shockwave")
                {
                    float shockWavePosX = (float)Convert.ToDouble(data["x"]); ;
                    float shockWavePosY = (float)Convert.ToDouble(data["y"]); ;
                    
                    Vector2 position = new Vector2(shockWavePosX, shockWavePosY);
                    member.ShockwavePositions.Add(position);
                    if (data.ContainsKey("teamCode"))
                    {
                        TeamCodes shockwaveCode = (TeamCodes)data["teamCode"];
                        member.ShockwaveTeamCodes.Add(shockwaveCode);

                        TeamCodes instigatorCode = (TeamCodes)data["instigator"];
                        member.InstigatorTeamCodes.Add(instigatorCode);
                    }
                }
            }

        }
    }

	private void OnRelaySystemMessage(string jsonResponse)
	{
        var json = JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
        if (json["op"] as string == "DISCONNECT")
        {
            if (json.ContainsKey("cxId"))
            {
                var profileId = json["cxId"] as string;
                Lobby lobby = GameManager.Instance.CurrentLobby;
                profileId = lobby.FormatCxIdToProfileId(profileId);
                foreach (var member in lobby.Members)
                {
                    if (member.ID == profileId)
                    {
                        member.IsAlive = false;
                        
						EmitSignal(SignalName.LobbyUpdated);
                        EmitSignal(SignalName.MatchUpdated);
                        break;
                    }
                }
            }
        }
        else if (json["op"] as string == "CONNECT")
        {
            //Check if user connected is new, if so update name to not have "In Lobby"
            EmitSignal(SignalName.MatchUpdated);
        }
        else if (json["op"] as string == "END_MATCH")
        {
            GameManager.Instance.IsReady = false;
            GameManager.Instance.CurrentUserInfo.PresentSinceStart = false;
   
			EmitSignal(SignalName.LobbyUpdated);
            EmitSignal(SignalName.MatchUpdated);
            EmitSignal(SignalName.MatchEnded);
        }
        else if (json["op"] as string == "MIGRATE_OWNER")
        {
            GameManager.Instance.CurrentLobby.ReassignOwnerID(_brainCloud.RelayService.OwnerCxId);
            
            EmitSignal(SignalName.LobbyUpdated);
            EmitSignal(SignalName.MatchUpdated);
        }
    }
}
