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

	private BrainCloudWrapper m_BrainCloud;
	private GameManager m_GameManager;

	private bool m_PresentWhileStarted;

	public override void _Ready()
	{
		m_GameManager = GetNode<GameManager>("/root/GameManager");
		
		// Create and initialize the BrainCloud wrapper
		m_BrainCloud = new BrainCloudWrapper();

		m_BrainCloud.Init(Constants.kBrainCloudServer, Constants.kBrainCloudAppSecret, Constants.kBrainCloudAppID, Constants.kBrainCloudAppVersion);

		//m_BrainCloud.Client.EnableLogging(true);
	}

	public override void _Process(double delta)
	{
		// Make sure you invoke this method in Update, or else you won't get any callbacks
		m_BrainCloud.RunCallbacks();
	}

	public void AuthenticateUniversal(string universalID, string password)
	{
		BrainCloud.FailureCallback failureCallback = (status, reasonCode, jsonError, cbObject) =>
		{
			GD.Print("Anonymous Authentication Failure :(\n" + jsonError);
			EmitSignal(SignalName.AuthenticationRequestFailed);
		};

		GameManager.Instance.CurrentUserInfo.Username = universalID;
		m_BrainCloud.AuthenticateUniversal(universalID, password, true, HandlePlayerState, failureCallback);
	}

	public void FindLobby(string lobbyType)
	{
		GD.Print("registering callback");
		m_BrainCloud.RTTService.RegisterRTTLobbyCallback(OnLobbyEvent);

		SuccessCallback enableRTTSuccessCallback = (responseData, cbObject) =>
		{
			GD.Print("RTT Enabled!");

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
			GD.Print("colour: " + (int)GameManager.Instance.CurrentUserInfo.UserGameColor);
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
				GD.Print(string.Format("Failed | {0}  {1}  {2}", status, code, error));
			};

			m_BrainCloud.LobbyService.FindOrCreateLobby(lobbyType, 0, 1, algo, filterJson, 0, false, extra, teamCode, settings, null, successCallback, failureCallback);
		};

		FailureCallback enableRTTFailureCallback = (status, code, error, cbObject) =>
		{
			GD.Print(string.Format("[EnableRTT Failed] {0}  {1}  {2}", status, code, error));
		};

		// Real-time Tech (RTT) must be checked on the dashboard, under Design | Core App Info | Advanced Settings.
		m_BrainCloud.RTTService.EnableRTT(RTTConnectionType.WEBSOCKET, enableRTTSuccessCallback, enableRTTFailureCallback);
	}

	public void UpdateReady(Dictionary<string, object> extraJson)
	{
		m_BrainCloud.LobbyService.UpdateReady(GameManager.Instance.CurrentLobby.LobbyID, GameManager.Instance.IsReady, extraJson);
	}

	private void HandlePlayerState(string jsonResponse, object cobject)
	{
		GD.Print("HandlePlayerState");
		var response = JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
		var data = response["data"] as Dictionary<string, object>;
		var tempUsername = GameManager.Instance.CurrentUserInfo.Username;
		var userInfo = GameManager.Instance.CurrentUserInfo;
		userInfo = new UserInfo();
		userInfo.ID = data["profileId"] as string;

		SuccessCallback successCallback = (response, cbObject) =>
		{
			GD.Print(string.Format("Success | {0}", response));
			EmitSignal(SignalName.AuthenticationRequestCompleted);
		};
		FailureCallback failureCallback = (status, code, error, cbObject) =>
		{
			GD.Print(string.Format("Failed | {0}  {1}  {2}", status, code, error));
		};

		// If no username is set for this user, ask for it
		if (!data.ContainsKey("playerName"))
		{
			// Update name for display
			m_BrainCloud.PlayerStateService.UpdateName(tempUsername, successCallback, failureCallback);
		}
		else
		{
			userInfo.Username = data["playerName"] as string;
			if (string.IsNullOrEmpty(userInfo.Username))
			{
				userInfo.Username = tempUsername;
			}
			m_BrainCloud.PlayerStateService.UpdateName(userInfo.Username, successCallback, failureCallback);
		}
		GameManager.Instance.CurrentUserInfo = userInfo;
	}

    // Connect to the Relay server and start the game
    private void ConnectRelay()
    {
        m_PresentWhileStarted = false;
        m_BrainCloud.RTTService.RegisterRTTLobbyCallback(OnLobbyEvent);
        m_BrainCloud.RelayService.RegisterRelayCallback(OnRelayMessage);
        m_BrainCloud.RelayService.RegisterSystemCallback(OnRelaySystemMessage);

        int port = 0;
        GameManager.Instance.Protocol = RelayConnectionType.WEBSOCKET; // TODO:  add more options
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
            GD.Print(string.Format("Relay Connect Success | {0}", response));
            EmitSignal(SignalName.ConnectedToRelay);
        };
        FailureCallback failureCallback = (status, code, error, cbObject) =>
        {
            GD.Print(string.Format("Relay Connect Failed | {0}  {1}  {2}", status, code, error));
        };

        GD.Print("Sending connect relay request");
        m_BrainCloud.RelayService.Connect(connectionType, options, successCallback, failureCallback);
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
            //Debug.LogWarning("COULD NOT SERIALIZE " + jsonMessage);
        }
        return toDict;
    }

    private void OnLobbyEvent(string jsonResponse)
	{
		// TODO
		GD.Print("Lobby Event received" + jsonResponse);
        Dictionary<string, object> response = JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
        Dictionary<string, object> jsonData = response["data"] as Dictionary<string, object>;

		// If there is a lobby object present in the message, update our lobby
        // state with it.
        if (jsonData.ContainsKey("lobby"))
        {
            GameManager.Instance.CurrentLobby = new Lobby(jsonData["lobby"] as Dictionary<string, object>, jsonData["lobbyId"] as string);

            EmitSignal(SignalName.FoundLobby);

            //TODO:  GameManager.Instance.UpdateMatchAndLobbyState();
            EmitSignal(SignalName.LobbyUpdated);
            EmitSignal(SignalName.MatchUpdated);
        }

        //Using the key "operation" to determine what state the lobby is in
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
                            //CloseGame(true);
                        }
                        break;
                    }
                case "STARTING":
                    // Save our picked color index
                    m_PresentWhileStarted = true;
                    GameManager.Instance.UpdatePresentSinceStart();
                    //Settings.SetPlayerPrefColor(GameManager.Instance.CurrentUserInfo.UserGameColor);
                    
                    break;
                case "ROOM_READY":
                    GameManager.Instance.CurrentServer = new Server(jsonData);
                    // TODO:  GameManager.Instance.UpdateMatchAndLobbyState();
                    EmitSignal(SignalName.LobbyUpdated);
                    EmitSignal(SignalName.MatchUpdated);
                    //EmitSignal(SignalName.CursorPartyUpdated);
                    //Check to see if a user joined the lobby before the match started or after.
                    //If a user joins while match is in progress, you will only receive MEMBER_JOIN & ROOM_READY RTT updates.
                    if (m_PresentWhileStarted)
                    {
                        GD.Print("Connect relay");
                        ConnectRelay();
                    }
                    else
                    {
                        // TODO:  show JOIN GAME button
						//GameManager.Instance.JoinInProgressButton.gameObject.SetActive(true);
                    }
                    break;
            }
        }
    }

	private void OnRelayMessage(short netId, byte[] jsonResponse)
	{
        var memberProfileId = m_BrainCloud.RelayService.GetProfileIdForNetId(netId);

        var json = DeserializeString(jsonResponse);
        Lobby lobby = GameManager.Instance.CurrentLobby;
        foreach (var member in lobby.Members)
        {
            if (member.ID == memberProfileId)
            {
                var data = json["data"] as Dictionary<string, object>;
                if (data == null)
                {
                    //Debug.LogWarning("On Relay Message is null !");
                    break;
                }
                var op = json["op"] as string;
                if (op == "move")
                {
                    //member.IsAlive = true;
                    //float mousePosX = (float)Convert.ToDouble(data["x"]);
                    //float mousePosY = (float)Convert.ToDouble(data["y"]);

                    //member.MousePosition.y = mousePosY;
                    //member.MousePosition.x = mousePosX;
                }
                else if (op == "shockwave")
                {
                    ////Debug.Log("op == shockwave");
                    //Vector2 position;
                    //position.x = (float)Convert.ToDouble(data["x"]);
                    //position.y = (float)Convert.ToDouble(data["y"]);
                    //member.ShockwavePositions.Add(position);
                    //if (data.ContainsKey("teamCode"))
                    //{
                    //    TeamCodes shockwaveCode = (TeamCodes)data["teamCode"];
                    //    member.ShockwaveTeamCodes.Add(shockwaveCode);

                    //    TeamCodes instigatorCode = (TeamCodes)data["instigator"];
                    //    member.InstigatorTeamCodes.Add(instigatorCode);
                    //}
                }
            }

        }
    }

	private void OnRelaySystemMessage(string jsonResponse)
	{
        GD.Print("RelaySystemMessage");
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
                        // TODO:  GameManager.Instance.UpdateMatchAndLobbyState();
						EmitSignal(SignalName.LobbyUpdated);
                        EmitSignal(SignalName.MatchUpdated);
                        break;
                    }
                }
            }
        }
        else if (json["op"] as string == "CONNECT")
        {
            GD.Print("received CONNECT");
            //Check if user connected is new, if so update name to not have "In Lobby"
            EmitSignal(SignalName.MatchUpdated);
        }
        else if (json["op"] as string == "END_MATCH")
        {
            GameManager.Instance.IsReady = false;
            GameManager.Instance.CurrentUserInfo.PresentSinceStart = false;
            //TODO:  GameManager.Instance.UpdateMatchAndLobbyState();
			EmitSignal(SignalName.LobbyUpdated);
            EmitSignal(SignalName.MatchUpdated);
            //StateManager.Instance.ChangeState(GameStates.Lobby); TODO:  go to lobby?
        }
        else if (json["op"] as string == "MIGRATE_OWNER")
        {
            GameManager.Instance.CurrentLobby.ReassignOwnerID(m_BrainCloud.RelayService.OwnerCxId);
            // TODO:  GameManager.Instance.UpdateMatchAndLobbyState();
            EmitSignal(SignalName.LobbyUpdated);
            EmitSignal(SignalName.MatchUpdated);
        }
    }
}
