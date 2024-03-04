using Godot;
using System;
using System.Collections.Generic;

public partial class PreLobby : VBoxContainer
{
    [Signal]
    public delegate void FindLobbyRequestedEventHandler();

    private BCManager _bcManager;

    private OptionButton _lobbyTypeOptionButton;
    private Button _logOutButton;
    private Button _playButton;

    public override void _Ready()
    {
        _bcManager = GetNode<BCManager>("/root/BCManager");

        _lobbyTypeOptionButton = GetNode<OptionButton>("HBoxContainer/LobbyOptionButton");
        _logOutButton = GetNode<Button>("MenuButtons/LogOutButton");
        _playButton = GetNode<Button>("MenuButtons/PlayButton");
        _playButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnPlayClicked));
    }

    private void OnPlayClicked()
    {
        string lobbyType = _lobbyTypeOptionButton.Text;

        if (!string.IsNullOrEmpty(lobbyType))
        {
            if (lobbyType == "TeamCursorPartyV2" || lobbyType == "TeamCursorPartyV2")
            {
                GameManager.Instance.Mode = GameManager.GameMode.Team;
            }

            EmitSignal(SignalName.FindLobbyRequested);
            _bcManager.FindLobby(lobbyType);
        }
    }
}
