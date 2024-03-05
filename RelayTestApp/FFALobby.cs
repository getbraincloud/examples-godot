using Godot;
using System;
using System.Collections.Generic;

public partial class FFALobby : Control
{
    [Signal]
    public delegate void StartMatchRequestedEventHandler();
    [Signal]
    public delegate void LeaveLobbyRequestedEventHandler();
    [Signal]
    public delegate void JoinMatchRequestedEventHandler();

    private BCManager _bcManager;

    private Button _blackButton;
    private Button _purpleButton;
    private Button _greyButton;
    private Button _orangeButton;
    private Button _blueButton;
    private Button _greenButton;
    private Button _yellowButton;
    private Button _cyanButton;

    private VBoxContainer _lobbyMembersContainer;
    private Label _lobbyIDLabel;
    private Button _leaveButton;
    private Button _startButton;
    private Button _joinButton;

    public override void _Ready()
    {
        _bcManager = GetNode<BCManager>("/root/BCManager");

        _blackButton = GetNode<Button>("ColourButtons/BlackButton");
        _purpleButton = GetNode<Button>("ColourButtons/PurpleButton");
        _greyButton = GetNode<Button>("ColourButtons/GreyButton");
        _orangeButton = GetNode<Button>("ColourButtons/OrangeButton");
        _blueButton = GetNode<Button>("ColourButtons/BlueButton");
        _greenButton = GetNode<Button>("ColourButtons/GreenButton");
        _yellowButton = GetNode<Button>("ColourButtons/YellowButton");
        _cyanButton = GetNode<Button>("ColourButtons/CyanButton");
        

        _lobbyMembersContainer = GetNode<VBoxContainer>("LobbyMembers");

        _leaveButton = GetNode<Button>("LobbyButtons/LeaveButton");
        _leaveButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLeaveButtonPressed));

        _startButton = GetNode<Button>("LobbyButtons/StartButton");
        _startButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnStartButtonPressed));
        _startButton.Hide();

        _joinButton = GetNode<Button>("LobbyButtons/JoinButton");
        _joinButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnJoinButtonPressed));
        _joinButton.Hide();

        _lobbyIDLabel = GetNode<Label>("LobbyIDLabel");
        _lobbyIDLabel.Hide();
    }

    public void ShowStartButton(bool show)
    {
        if (show)
        {
            _startButton.Show();
        }

        else { _startButton.Hide(); }
    }

    public void ShowJoinButton()
    {
        _joinButton.Show();
    }

    public void DisplayLobbyID()
    {
        if (!string.IsNullOrEmpty(GameManager.Instance.CurrentLobby.LobbyID))
        {
            _lobbyIDLabel.Text = "Lobby ID: " + GameManager.Instance.CurrentLobby.LobbyID;
            _lobbyIDLabel.Show();
        }
    }

    public void AddLobbyMember(string name, GameManager.GameColors colour, bool memberIsHost)
    {
        var lobbyMemberScene = GD.Load<PackedScene>("res://LobbyMember.tscn");
        LobbyMember lobbyMember = (LobbyMember)lobbyMemberScene.Instantiate();
        _lobbyMembersContainer.AddChild(lobbyMember);
        lobbyMember.SetName(name);
        lobbyMember.SetColour(colour);
        lobbyMember.SetHostIcon(memberIsHost);
    }

    public void ClearLobbyMembers()
    {
        foreach(Node lobbyMember in _lobbyMembersContainer.GetChildren())
        {
            lobbyMember.QueueFree();
        }
    }

    private void OnColourButtonPressed(int buttonIndex)
    {
        GameManager.GameColors newColour = (GameManager.GameColors)buttonIndex;

        GameManager.Instance.CurrentUserInfo.UserGameColor = newColour;

        // Send update to brainCloud
        Dictionary<string, object> extra = new Dictionary<string, object>();
        extra["colorIndex"] = (int)GameManager.Instance.CurrentUserInfo.UserGameColor;
        extra["presentSinceStart"] = GameManager.Instance.CurrentUserInfo.PresentSinceStart;
        
        _bcManager.UpdateReady(extra);

    }

    private void OnLeaveButtonPressed()
    {
        EmitSignal(SignalName.LeaveLobbyRequested);
    }

    private void OnStartButtonPressed()
    {
        EmitSignal(SignalName.StartMatchRequested);
        
        GameManager.Instance.IsReady = true;

        // Setting up a update to send to brain cloud about local users color
        var extra = new Dictionary<string, object>();
        extra["colorIndex"] = (int)GameManager.Instance.CurrentUserInfo.UserGameColor;
        extra["presentSinceStart"] = GameManager.Instance.CurrentUserInfo.PresentSinceStart;

        _bcManager.UpdateReady(extra);
    }

    private void OnJoinButtonPressed()
    {
        EmitSignal(SignalName.JoinMatchRequested);
    }
}
