using Godot;
using System;
using System.Collections.Generic;

public partial class PreLobby : VBoxContainer
{
    [Signal]
    public delegate void FindLobbyRequestedEventHandler();

    private BCManager m_BCManager;

    private Label m_UserLabel;
    private OptionButton m_LobbyTypeOptionButton;
    private Button m_LogOutButton;
    private Button m_PlayButton;

    public override void _Ready()
    {
        m_BCManager = GetNode<BCManager>("/root/BCManager");

        m_UserLabel = GetNode<Label>("UserLabel");
        m_LobbyTypeOptionButton = GetNode<OptionButton>("HBoxContainer/LobbyOptionButton");
        m_LogOutButton = GetNode<Button>("MenuButtons/LogOutButton");
        m_PlayButton = GetNode<Button>("MenuButtons/PlayButton");
        m_PlayButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnPlayClicked));
    }

    private void OnPlayClicked()
    {
        string lobbyType = m_LobbyTypeOptionButton.Text;

        if (!string.IsNullOrEmpty(lobbyType))
        {
            if (lobbyType == "TeamCursorPartyV2" || lobbyType == "TeamCursorPartyV2")
            {
                GameManager.Instance.Mode = GameManager.GameMode.Team;
            }

            EmitSignal(SignalName.FindLobbyRequested);
            m_BCManager.FindLobby(lobbyType);
        }
    }
}
