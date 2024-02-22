using Godot;
using System;
using System.Collections.Generic;

public partial class FFAGame : Control
{   
    private BCManager m_BCManager;

    private VBoxContainer _playersContainer;
    private TextureRect _gameSide;
    
    private readonly List<Cursor> _userCursorsList = new List<Cursor>();
    private bool _inGameArea = false;

    public override void _Ready()
    {
        m_BCManager = GetNode<BCManager>("/root/BCManager");
        m_BCManager.Connect(BCManager.SignalName.MatchUpdated, new Callable(this, MethodName.OnMatchUpdated));

        _playersContainer = GetNode<VBoxContainer>("GameScreen/GameInfo/PlayerInfo/PlayersContainer");
        _gameSide = GetNode<TextureRect>("GameScreen/Game/TextureRect");
        _gameSide.Connect(TextureRect.SignalName.MouseEntered, new Callable(this, MethodName.OnMouseEnteredGameArea));
        _gameSide.Connect(TextureRect.SignalName.MouseExited, new Callable(this, MethodName.OnMouseExitedGameArea));
        //_gameArea = GetNode<Control>("GameScreen/Game/GameArea");
    }

    public override void _Input(InputEvent @event)
    {
        if (_inGameArea)
        {
            // Mouse in viewport coordinates.
            if (@event is InputEventMouseButton eventMouseButton)
            {
                GD.Print("Mouse Click/Unclick at: ", eventMouseButton.Position);
            }
                
            else if (@event is InputEventMouseMotion eventMouseMotion)
            {
                GD.Print("Mouse Motion at: ", eventMouseMotion.Position);
            }

            //if (@event is InputEventMouse)
            //{
            //    Vector2 memberPosition = (@event as InputEventMouse).Position;
            //    string eventType = "";

            //    if (@event is InputEventMouseButton eventMouseButton && eventMouseButton.Pressed)
            //    {
            //        //GD.Print("Mouse Click/Unclick at: ", eventMouseButton.Position);

            //        memberPosition = eventMouseButton.Position;
            //        eventType = "click";
            //    }
            //    else if (@event is InputEventMouseMotion eventMouseMotion)
            //    {
            //        //GD.Print("Mouse Motion at: ", eventMouseMotion.Position);

            //        memberPosition = eventMouseMotion.Position;
            //        eventType = "move";
            //    }
            //}
        }
    }

    public void AddPlayer(string name, GameManager.GameColors colour)
    {
        var playerLabel = new Label();
        playerLabel.Text = name;
        _playersContainer.AddChild(playerLabel);
    }

    public void ClearPlayerss()
    {
        foreach (Node playerLabel in _playersContainer.GetChildren())
        {
            playerLabel.QueueFree();
        }

        //foreach (Node cursor in _gameArea.GetChildren())
        //{
        //    cursor.QueueFree();
        //}
    }

    public void AddCursor(string name, int colourIndex)
    {
        var cursor = GD.Load<Cursor>("res://Cursor.tscn");
        
        cursor.SetPlayerName(name);
        cursor.SetCursorColour(colourIndex);
        
    }

    public void ClearCursors()
    {
        //foreach (Node cursor in _gameArea.GetChildren())
        //{
        //    cursor.QueueFree();
        //}
    }

    private void OnMatchUpdated()
    {
        GD.Print("OnMatchUpdated");
        ClearPlayerss();
        //ClearCursors();
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

                // Add Cursor to GameArea
                //var cursor = GD.Load<PackedScene>("res://Cursor.tscn");
                //Cursor playerCursor = (Cursor)cursor.Instantiate();
                
                //AddChild(playerCursor);
                //playerCursor.SetPlayerName(memberName);
                //playerCursor.SetCursorColour((int)lobby.Members[i].UserGameColor);
            }
        }
    }

    private void OnMouseEnteredGameArea()
    {
        GD.Print("Mouse in GameArea");
        _inGameArea = true;
    }

    private void OnMouseExitedGameArea()
    {
        GD.Print("Mouse left GameArea");
        _inGameArea = false;
    }
}
