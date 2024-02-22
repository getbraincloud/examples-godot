using Godot;
using System;
using System.Runtime.CompilerServices;

public partial class Main : Control
{
	private MarginContainer m_CurrentSceneContainer;

	private LoadingScreen _loadingScreen;
	private Authentication m_AuthenticationScene;
	private PreLobby m_PreLobby;
	private FFALobby m_FFALobby;
	private TeamLobby m_TeamLobby;
	private FFAGame m_FFAGame;

	private Node m_CurrentScene;

	private BCManager m_BCManager;

	// TODO:  don't like this
	private bool _findingMatch;

	public override void _Ready()
	{
		m_BCManager = GetNode<BCManager>("/root/BCManager");
		m_BCManager.Connect(BCManager.SignalName.AuthenticationRequestCompleted, new Callable(this, MethodName.OnAuthenticated));
		m_BCManager.Connect(BCManager.SignalName.FoundLobby, new Callable(this, MethodName.OnLobbyFound));
		m_BCManager.Connect(BCManager.SignalName.LobbyUpdated, new Callable(this, MethodName.OnLobbyUpdated));
		m_BCManager.Connect(BCManager.SignalName.ConnectedToRelay, new Callable(this, MethodName.OnConnectedToRelay));

		m_CurrentSceneContainer = GetNode<MarginContainer>("CurrentSceneContainer");

		// Go to Authentication Scene first
		var authenticationScene = GD.Load<PackedScene>("res://Authentication.tscn");
		m_AuthenticationScene = (Authentication)authenticationScene.Instantiate();
		ChangeScene(m_AuthenticationScene);
		m_AuthenticationScene.Connect(Authentication.SignalName.AuthenticationRequested, new Callable(this, MethodName.OnAuthenticationRequested));
	}

	private void ChangeScene(Node scene)
	{
		if (m_CurrentScene != null)
		{
			m_CurrentScene.QueueFree();
		}

		m_CurrentScene = scene;

		m_CurrentSceneContainer.AddChild(m_CurrentScene);
	}

	private void OnAuthenticationRequested()
	{
		GD.Print("Authenticating...");

		var loadingScreen = GD.Load<PackedScene>("res://LoadingScreen.tscn");
		_loadingScreen = (LoadingScreen)loadingScreen.Instantiate();
		_loadingScreen.SetLoadingMessage("Authenticating . . .");
		ChangeScene(_loadingScreen);
	}

	private void OnAuthenticated()
	{
		var preLobbyScene = GD.Load<PackedScene>("res://PreLobby.tscn");
		m_PreLobby = (PreLobby)preLobbyScene.Instantiate();
		ChangeScene(m_PreLobby);
		m_PreLobby.Connect(PreLobby.SignalName.FindLobbyRequested, new Callable(this, MethodName.OnFindLobbyRequested));
	}

	private void OnFindLobbyRequested()
	{
        var loadingScreen = GD.Load<PackedScene>("res://LoadingScreen.tscn");
        _loadingScreen = (LoadingScreen)loadingScreen.Instantiate();
        _loadingScreen.SetLoadingMessage("Finding Lobby . . .");
        ChangeScene(_loadingScreen);
    }

	private void OnLobbyFound()
	{
		if(!_findingMatch)
		{
            switch (GameManager.Instance.Mode)
            {
                case GameManager.GameMode.FreeForAll:
                    if (m_FFALobby == null)
                    {
                        GD.Print("Changing to Lobby Scene");
                        var ffaLobby = GD.Load<PackedScene>("res://FFALobby.tscn");
                        m_FFALobby = (FFALobby)ffaLobby.Instantiate();
                        ChangeScene(m_FFALobby);
                        m_FFALobby.Connect(FFALobby.SignalName.StartMatchRequested, new Callable(this, MethodName.OnStartMatchRequested));
                    }
                    break;
                case GameManager.GameMode.Team:
                    if (m_TeamLobby == null)
                    {
                        var teamLobby = GD.Load<PackedScene>("res://TeamLobby.tscn");
                        m_TeamLobby = (TeamLobby)teamLobby.Instantiate();
                        ChangeScene(m_TeamLobby);
                    }
                    break;
                default:
                    break;
            }
        }
	}

	private void OnLobbyUpdated()
	{
		if(m_FFALobby != null)
		{
			GD.Print("OnLobbyUpdate");
			if (GameManager.Instance.Mode == GameManager.GameMode.FreeForAll)
			{
				m_FFALobby.DisplayLobbyID();
				m_FFALobby.ClearLobbyMembers();
				Lobby lobby = GameManager.Instance.CurrentLobby;
				for (int i = 0; i < lobby.Members.Count; i++)
				{
					if (lobby.Members[i].IsAlive)
					{
						string memberName = lobby.Members[i].Username;
						if (lobby.Members[i].IsHost)
						{
							memberName += " (HOST)";
						}

						GameManager.GameColors memberColour = lobby.Members[i].UserGameColor;

						m_FFALobby.AddLobbyMember(memberName, memberColour);
					}
				}
			}
		}
	}

	private void OnStartMatchRequested()
	{
		_findingMatch = true;
        var loadingScreen = GD.Load<PackedScene>("res://LoadingScreen.tscn");
        _loadingScreen = (LoadingScreen)loadingScreen.Instantiate();
        _loadingScreen.SetLoadingMessage("Starting Match . . .");
        ChangeScene(_loadingScreen);
        m_FFALobby = null;
    }

	private void OnConnectedToRelay()
	{
		var ffaGame = GD.Load<PackedScene>("res://FFAGame.tscn");
		m_FFAGame = (FFAGame)ffaGame.Instantiate();
		ChangeScene(m_FFAGame);
	}
}
