using Godot;
using System;
using System.Collections.Generic;

public partial class PreLobby : VBoxContainer
{
    [Signal]
    public delegate void FindLobbyRequestedEventHandler();
    [Signal]
    public delegate void LogOutRequestedEventHandler();

    private BCManager _bcManager;

    private Label _currentUserLabel;
    private OptionButton _lobbyTypeOptionButton;
    private Button _logOutButton;
    private Button _playButton;

    public override void _Ready()
    {
        _bcManager = GetNode<BCManager>("/root/BCManager");

        _currentUserLabel = GetNode<Label>("CurrentUserLabel");
        _currentUserLabel.Text = "Welcome, " + GameManager.Instance.CurrentUserInfo.Username;

        _lobbyTypeOptionButton = GetNode<OptionButton>("HBoxContainer/LobbyOptionButton");

        _logOutButton = GetNode<Button>("MenuButtons/LogOutButton");
        _logOutButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLogOutClicked));

        _playButton = GetNode<Button>("MenuButtons/PlayButton");
        _playButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnPlayClicked));
    }

    private void OnLogOutClicked()
    {
        EmitSignal(SignalName.LogOutRequested);
        _bcManager.LogOut();
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
