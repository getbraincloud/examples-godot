using Godot;
using System;
using System.Runtime.CompilerServices;

public partial class Main : Control
{
	private MarginContainer m_CurrentSceneContainer;

	private LoadingScreen _loadingScreen;
	private Authentication _authenticationScene;
	private PreLobby _preLobby;
	private FFALobby _ffaLobby;
	private TeamLobby _teamLobby;
	private FFAGame _ffaGame;

	private Node _currentScene;

	private BCManager _bcManager;

	private bool _findingMatch;

	public override void _Ready()
	{
		_bcManager = GetNode<BCManager>("/root/BCManager");
		_bcManager.Connect(BCManager.SignalName.AuthenticationRequestCompleted, new Callable(this, MethodName.OnAuthenticated));
		_bcManager.Connect(BCManager.SignalName.FoundLobby, new Callable(this, MethodName.OnLobbyFound));
		_bcManager.Connect(BCManager.SignalName.LobbyUpdated, new Callable(this, MethodName.OnLobbyUpdated));
		_bcManager.Connect(BCManager.SignalName.ConnectedToRelay, new Callable(this, MethodName.OnConnectedToRelay));
		_bcManager.Connect(BCManager.SignalName.FoundGameInProgress, new Callable(this, MethodName.OnFoundGameInProgress));
		_bcManager.Connect(BCManager.SignalName.LeaveLobbyReady, new Callable(this, MethodName.OnLeaveLobbyReady));
		_bcManager.Connect(BCManager.SignalName.MatchEnded, new Callable(this, MethodName.OnMatchEnded));
		_bcManager.Connect(BCManager.SignalName.StartingMatch, new Callable(this, MethodName.OnStartMatchRequested));

		m_CurrentSceneContainer = GetNode<MarginContainer>("CurrentSceneContainer");

		// Go to Authentication Scene first
		var authenticationScene = GD.Load<PackedScene>("res://Authentication.tscn");
		_authenticationScene = (Authentication)authenticationScene.Instantiate();
		ChangeScene(_authenticationScene);
		_authenticationScene.Connect(Authentication.SignalName.AuthenticationRequested, new Callable(this, MethodName.OnAuthenticationRequested));
	}

	private void ChangeScene(Node scene)
	{
		if (_currentScene != null)
		{
			_currentScene.QueueFree();
		}

		_currentScene = scene;

		m_CurrentSceneContainer.AddChild(_currentScene);
	}

	private void GoToPreLobby()
	{
        var preLobbyScene = GD.Load<PackedScene>("res://PreLobby.tscn");
        _preLobby = (PreLobby)preLobbyScene.Instantiate();
        ChangeScene(_preLobby);
        _preLobby.Connect(PreLobby.SignalName.FindLobbyRequested, new Callable(this, MethodName.OnFindLobbyRequested));
    }

	private void GoToLobby()
	{
		if(GameManager.Instance.Mode == GameManager.GameMode.FreeForAll)
		{
            var ffaLobby = GD.Load<PackedScene>("res://FFALobby.tscn");
            _ffaLobby = (FFALobby)ffaLobby.Instantiate();
            ChangeScene(_ffaLobby);
            _ffaLobby.Connect(FFALobby.SignalName.StartMatchRequested, new Callable(this, MethodName.OnStartMatchRequested));
			_ffaLobby.Connect(FFALobby.SignalName.JoinMatchRequested, new Callable(this, MethodName.OnJoinMatchRequested));
            _ffaLobby.Connect(FFALobby.SignalName.LeaveLobbyRequested, new Callable(this, MethodName.OnLeaveLobbyRequested));
        }
		else if(GameManager.Instance.Mode == GameManager.GameMode.Team)
		{
            var teamLobby = GD.Load<PackedScene>("res://TeamLobby.tscn");
            _teamLobby = (TeamLobby)teamLobby.Instantiate();
            ChangeScene(_teamLobby);
        }
	}

	private void Disconnect()
	{
        var loadingScreen = GD.Load<PackedScene>("res://LoadingScreen.tscn");
        _loadingScreen = (LoadingScreen)loadingScreen.Instantiate();
        _loadingScreen.SetLoadingMessage("Disconnecting . . .");
        ChangeScene(_loadingScreen);
        _bcManager.LeaveGame();
    }

	private void OnAuthenticationRequested()
	{
		var loadingScreen = GD.Load<PackedScene>("res://LoadingScreen.tscn");
		_loadingScreen = (LoadingScreen)loadingScreen.Instantiate();
		_loadingScreen.SetLoadingMessage("Authenticating . . .");
		ChangeScene(_loadingScreen);
	}

	private void OnAuthenticated()
	{
		GoToPreLobby();
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
			GoToLobby();
        }
	}

	private void OnLobbyUpdated()
	{
		if(_ffaLobby != null)
		{
			bool memberIsHost = false;

			if (GameManager.Instance.Mode == GameManager.GameMode.FreeForAll)
			{
				_ffaLobby.ShowStartButton(GameManager.Instance.IsLocalUserHost());
				_ffaLobby.DisplayLobbyID();
				_ffaLobby.ClearLobbyMembers();
				Lobby lobby = GameManager.Instance.CurrentLobby;
				for (int i = 0; i < lobby.Members.Count; i++)
				{
					memberIsHost = false;
					if (lobby.Members[i].IsAlive)
					{
						string memberName = lobby.Members[i].Username;
						if (lobby.Members[i].IsHost)
						{
							memberIsHost = true;
						}

						GameManager.GameColors memberColour = lobby.Members[i].UserGameColor;

						_ffaLobby.AddLobbyMember(memberName, memberColour, memberIsHost);
					}
				}
			}
		}
	}

	private void OnLeaveLobbyRequested()
	{
        _ffaLobby = null;
		_teamLobby = null;
		Disconnect();
	}

	private void OnLeaveLobbyReady()
	{
		GoToPreLobby();
	}

	private void OnMatchEnded()
	{
        var loadingScreen = GD.Load<PackedScene>("res://LoadingScreen.tscn");
        _loadingScreen = (LoadingScreen)loadingScreen.Instantiate();
        _loadingScreen.SetLoadingMessage("Ending Match . . .");
        ChangeScene(_loadingScreen);
    }

	private void OnLeaveMatchRequested()
	{
		_ffaGame = null;
		Disconnect();
	}

	private void OnFoundGameInProgress()
	{
		if(_ffaLobby != null)
		{
			_ffaLobby.ShowJoinButton();
		}
	}

	private void OnStartMatchRequested()
	{
		_findingMatch = true;
        var loadingScreen = GD.Load<PackedScene>("res://LoadingScreen.tscn");
        _loadingScreen = (LoadingScreen)loadingScreen.Instantiate();
        _loadingScreen.SetLoadingMessage("Starting Match . . .");
        ChangeScene(_loadingScreen);
        _ffaLobby = null;
    }

	private void OnConnectedToRelay()
	{
		var ffaGame = GD.Load<PackedScene>("res://FFAGame.tscn");
		_ffaGame = (FFAGame)ffaGame.Instantiate();
		ChangeScene(_ffaGame);

		_ffaGame.ShowEndMatch(GameManager.Instance.IsLocalUserHost());

		_ffaGame.Connect(FFAGame.SignalName.LeaveMatchRequested, new Callable(this, MethodName.OnLeaveMatchRequested));
		_findingMatch = false;
	}

	private void OnJoinMatchRequested()
	{
        var loadingScreen = GD.Load<PackedScene>("res://LoadingScreen.tscn");
        _loadingScreen = (LoadingScreen)loadingScreen.Instantiate();
        _loadingScreen.SetLoadingMessage("Joining Match . . .");
        ChangeScene(_loadingScreen);
        _bcManager.JoinMatch();
	}
}
