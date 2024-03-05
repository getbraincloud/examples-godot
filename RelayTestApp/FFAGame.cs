using Godot;
using System;
using System.Collections.Generic;
using static GameManager;

public partial class FFAGame : Control
{
    [Signal]
    public delegate void LeaveMatchRequestedEventHandler();

    private BCManager _bcManager;

    private VBoxContainer _playersContainer;
    private MarginContainer _gameArea;
    private CursorParty _cursorParty;

    private Button _endMatchButton;
    private Button _leaveMatchButton;

    private readonly List<Cursor> _userCursorsList = new List<Cursor>();
    private bool _inGameArea = false;

    protected List<Vector2> _localShockwavePositions = new List<Vector2>();
    protected List<TeamCodes> _localShockwaveCodes = new List<TeamCodes>();

    public override void _Ready()
    {
        _bcManager = GetNode<BCManager>("/root/BCManager");
        _bcManager.Connect(BCManager.SignalName.MatchUpdated, new Callable(this, MethodName.OnMatchUpdated));
        _bcManager.Connect(BCManager.SignalName.CursorPartyUpdated, new Callable(this, MethodName.OnCursorPartyUpdated));

        _playersContainer = GetNode<VBoxContainer>("HBoxContainer/PlayerInfo/PlayersContainer");

        _gameArea = GetNode<MarginContainer>("HBoxContainer/GameSide/VBoxContainer/GameArea");

        _cursorParty = GetNode<CursorParty>("HBoxContainer/GameSide/VBoxContainer/GameArea/CursorParty");
        _cursorParty.Connect(CursorParty.SignalName.MouseMoved, new Callable(this, MethodName.OnMouseMoved));
        _cursorParty.Connect(CursorParty.SignalName.MouseClicked, new Callable(this, MethodName.OnMouseClicked));

        _endMatchButton = GetNode<Button>("HBoxContainer/GameSide/VBoxContainer/Buttons/EndMatchButton");
        _endMatchButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnEndMatchButtonPressed));
        _endMatchButton.Hide();

        _leaveMatchButton = GetNode<Button>("HBoxContainer/GameSide/VBoxContainer/Buttons/LeaveGameButton");
        _leaveMatchButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLeaveMatchButtonPressed));
    }

    public override void _Process(double delta)
    {
        UpdateCursorPositions();
        UpdateShockwaves();
    }

    public void ShowEndMatch(bool show)
    {
        if(show) _endMatchButton.Show();
    }

    public void AddPlayer(string name, GameManager.GameColors colour)
    {
        var playerLabel = new Label();
        playerLabel.Text = name;
        _playersContainer.AddChild(playerLabel);
    }

    /// <summary>
    /// Remove all player name Labels
    /// </summary>
    public void ClearPlayerss()
    {
        foreach (Node playerLabel in _playersContainer.GetChildren())
        {
            playerLabel.QueueFree();
        }
    }

    /// <summary>
    /// Remove all instances of the Cursor scene
    /// </summary>
    public void ClearCursors()
    {
        if (_userCursorsList.Count <= 0) return;

        foreach (Node cursor in _gameArea.GetChildren())
        {
            if(cursor.GetType() == typeof(Cursor))
            cursor.QueueFree();
        }

        _userCursorsList.Clear();
    }

    /// <summary>
    /// Create Cursor scene instances for each lobby member and add them to the scene
    /// </summary>
    public void UpdateCursorList()
    {
        Lobby lobby = GameManager.Instance.CurrentLobby;
        ClearCursors();
        
        for (int i = 0; i < lobby.Members.Count; i++)
        {
            //Set up Cursor image
            var cursor = GD.Load<PackedScene>("res://Cursor.tscn");
            Cursor playerCursor = (Cursor)cursor.Instantiate();

            _cursorParty.AddChild(playerCursor);

            string memberName = lobby.Members[i].Username;
            if (lobby.Members[i].IsHost)
            {
                memberName += " (HOST)";
            }
            playerCursor.SetName(memberName);
            playerCursor.SetColour((int)lobby.Members[i].UserGameColor);

            lobby.Members[i].UserCursor = playerCursor;

            _userCursorsList.Add(playerCursor);
            if (lobby.Members[i].Username == GameManager.Instance.CurrentUserInfo.Username)
            {
                playerCursor.SetUserCursor(true);
                int colourIndex = (int)GameManager.Instance.CurrentUserInfo.UserGameColor;
                GD.Print("Colour index for path: " + colourIndex);

                _cursorParty.SetCustomCursor("res://Cursors/arrow" + colourIndex + ".png");
            }
        }
    }

    private void OnEndMatchButtonPressed()
    {
        _bcManager.EndMatch();
    }

    private void OnLeaveMatchButtonPressed()
    {
        EmitSignal(SignalName.LeaveMatchRequested);
    }

    /// <summary>
    /// Set Position of each Cursor instance
    /// </summary>
    private void UpdateCursorPositions()
    {
        Lobby lobby = GameManager.Instance.CurrentLobby;
        for (int i = 0; i < lobby.Members.Count; i++)
        {
            if (lobby.Members[i].UserCursor == null)
            {
                UpdateCursorList();
            }

            Rect2 gameAreaRect = _cursorParty.GetGameAreaRect();

            Vector2 newMousePosition = new Vector2(
                gameAreaRect.Size.X * lobby.Members[i].MousePosition.X,
                gameAreaRect.Size.Y * lobby.Members[i].MousePosition.Y
            );

            lobby.Members[i].UserCursor.Position = newMousePosition;
        }
    }

    private void UpdateShockwaves()
    {
        Lobby lobby = GameManager.Instance.CurrentLobby;

        foreach (var member in lobby.Members)
        {
            if (member.AllowSendTo)
            {
                for (int i = 0; i < member.ShockwavePositions.Count; ++i)
                {
                    if (member.ShockwaveTeamCodes.Count > 0 && member.InstigatorTeamCodes.Count > 0)
                    {
                        CreateShockwave(member.ShockwavePositions[i], GameManager.ReturnUserColor(member.UserGameColor), member.ShockwaveTeamCodes[i], member.InstigatorTeamCodes[i]);
                    }
                    else
                    {
                        CreateShockwave(member.ShockwavePositions[i], GameManager.ReturnUserColor(member.UserGameColor));
                    }
                }
            }

            //Clear the list so there's no backlog of input positions
            if (member.ShockwavePositions.Count > 0)
            {
                member.ShockwavePositions.Clear();
                member.ShockwaveTeamCodes.Clear();
                member.InstigatorTeamCodes.Clear();
            }
        }

        if (GameManager.Instance.CurrentUserInfo.AllowSendTo)
        {
            int i = 0;
            foreach (var pos in _localShockwavePositions)
            {
                CreateShockwave
                (
                    pos,
                    GameManager.ReturnUserColor(GameManager.Instance.CurrentUserInfo.UserGameColor),
                    _localShockwaveCodes[i],
                    GameManager.Instance.CurrentUserInfo.Team
                );
                i++;
            }
        }

        //Clear the list so there's no backlog of input positions
        if (_localShockwavePositions.Count > 0)
        {
            _localShockwavePositions.Clear();
            _localShockwaveCodes.Clear();
        }
    }


    private void CreateShockwave(Vector2 position, Color waveColor, TeamCodes team = TeamCodes.all, TeamCodes instigatorTeam = TeamCodes.all)
    {
        var shockwave = GD.Load<PackedScene>("res://Shockwave.tscn");
        Shockwave playerShockwave = (Shockwave)shockwave.Instantiate();

        _cursorParty.AddChild(playerShockwave);

        playerShockwave.Modulate = waveColor;

        Rect2 gameAreaRect = _cursorParty.GetGameAreaRect();
        Vector2 shockwavePosition = new Vector2(
            gameAreaRect.Size.X * position.X,
            gameAreaRect.Size.Y * position.Y
        );

        playerShockwave.Position = shockwavePosition;

        
    }

    private void OnMatchUpdated()
    {
        ClearPlayerss();
        ClearCursors();
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

                // Add label to Player List
                var playerLabel = new Label();
                playerLabel.Text = memberName;
                _playersContainer.AddChild(playerLabel);
            }
        }
    }

    private void OnCursorPartyUpdated()
    {
        UpdateCursorList();
    }

    private void OnMouseMoved(Vector2 mousePosition)
    {
        GameManager.Instance.CurrentUserInfo.IsAlive = true;
        GameManager.Instance.CurrentUserInfo.MousePosition = mousePosition;
        Lobby lobby = GameManager.Instance.CurrentLobby;

        foreach (var user in lobby.Members)
        {
            if (GameManager.Instance.CurrentUserInfo.ID == user.ID)
            {
                //Save it for later !
                user.IsAlive = true;
                user.MousePosition = mousePosition;
                break;
            }
        }

        // Send to other players
        Dictionary<string, object> jsonData = new Dictionary<string, object>();
        jsonData["x"] = mousePosition.X;
        jsonData["y"] = mousePosition.Y;

        //Set up JSON to send
        Dictionary<string, object> json = new Dictionary<string, object>();
        json["op"] = "move";
        json["data"] = jsonData;

        _bcManager.SendRelayMessage(json);
    }

    private void OnMouseClicked(Vector2 mousePosition, MouseButton mouseButton)
    {
        // Send to other players
        Dictionary<string, object> jsonData = new Dictionary<string, object>();
        jsonData["x"] = mousePosition.X;
        jsonData["y"] = mousePosition.Y;
        jsonData["teamCode"] = GameManager.TeamCodes.all;
        jsonData["instigator"] = (int)GameManager.Instance.CurrentUserInfo.Team;

        Dictionary<string, object> json = new Dictionary<string, object>();
        json["op"] = "shockwave";
        json["data"] = jsonData;

        //Save position locally for us to spawn in UpdateShockwaves()
        _localShockwavePositions.Add(mousePosition);
        _localShockwaveCodes.Add(TeamCodes.all);

        _bcManager.SendRelayMessage(json);
    }
}
