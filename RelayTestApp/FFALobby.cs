using Godot;
using System;
using System.Collections.Generic;

public partial class FFALobby : Control
{
    [Signal]
    public delegate void StartMatchRequestedEventHandler();

    private BCManager m_BCManager;
    
    private VBoxContainer m_LobbyMembersContainer;
    private Label m_LobbyIDLabel;
    private Button m_StartButton;

    public override void _Ready()
    {
        m_BCManager = GetNode<BCManager>("/root/BCManager");

        m_LobbyMembersContainer = GetNode<VBoxContainer>("LobbyMembers");

        m_StartButton = GetNode<Button>("LobbyButtons/StartButton");
        m_StartButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnStartButtonPressed));

        m_LobbyIDLabel = GetNode<Label>("LobbyIDLabel");
        m_LobbyIDLabel.Hide();
    }

    public void DisplayLobbyID()
    {
        if (!string.IsNullOrEmpty(GameManager.Instance.CurrentLobby.LobbyID))
        {
            m_LobbyIDLabel.Text = "Lobby ID: " + GameManager.Instance.CurrentLobby.LobbyID;
            m_LobbyIDLabel.Show();
        }
    }

    public void AddLobbyMember(string name, GameManager.GameColors colour)
    {
        var lobbyMemberLabel = new Label();
        lobbyMemberLabel.Text = name;
        m_LobbyMembersContainer.AddChild(lobbyMemberLabel);
    }

    public void ClearLobbyMembers()
    {
        foreach(Node lobbyMember in m_LobbyMembersContainer.GetChildren())
        {
            lobbyMember.QueueFree();
        }
    }

    private void OnStartButtonPressed()
    {
        EmitSignal(SignalName.StartMatchRequested);
        
        GameManager.Instance.IsReady = true;

        //Setting up a update to send to brain cloud about local users color
        var extra = new Dictionary<string, object>();
        extra["colorIndex"] = (int)GameManager.Instance.CurrentUserInfo.UserGameColor;
        extra["presentSinceStart"] = GameManager.Instance.CurrentUserInfo.PresentSinceStart;

        m_BCManager.UpdateReady(extra);
    }
}
