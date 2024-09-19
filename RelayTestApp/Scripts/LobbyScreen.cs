using Godot;
using System.Collections.Generic;
using System;

public partial class LobbyScreen : Control
{
    [Signal]
    public delegate void ColourChangedEventHandler(int colourIndex);
    [Signal]
    public delegate void LeaveLobbyRequestedEventHandler();
    [Signal]
    public delegate void JoinMatchRequestedEventHandler();
    [Signal]
    public delegate void StartMatchRequestedEventHandler();

    // Holds/Displays members in lobby
    private VBoxContainer _lobbyMembersContainer;

    // Leave lobby (return to LobbySelect Screen)
    private Button _leaveButton;

    // Join a match that is already in progress (TODO:  requires JoinInProgress to be enabled in brainCloud portal)
    private Button _joinButton;

    // Start a match (visible ONLY to host / lobby owner)
    private Button _startButton;

    public override void _Ready()
    {
        _lobbyMembersContainer = GetNode<VBoxContainer>("VBoxContainer/LobbyMembersContainer");
        _leaveButton = GetNode<Button>("VBoxContainer/LobbyButtons/LeaveButton");
        _joinButton = GetNode<Button>("VBoxContainer/LobbyButtons/JoinButton");
        _startButton = GetNode<Button>("VBoxContainer/LobbyButtons/StartButton");

        // Connect Button listener(s)
        _leaveButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLeaveButtonPressed));
        _joinButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnJoinButtonPressed));
        _startButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnStartButtonPressed));

        // Hide buttons when not necessary
        _joinButton.Hide();
        _startButton.Hide();
    }

    /// <summary>
    /// Display the "JOIN MATCH" Button if there is a match has already started.
    /// </summary>
    /// <param name="matchInProgress">bool determining whether a match is in progress or not.</param>
    public void ToggleJoinButtonVisibility(bool matchInProgress)
    {
        _joinButton.Visible = matchInProgress;
    }

    /// <summary>
    /// Display the "START MATCH" Button if the user is the host / lobby owner.
    /// </summary>
    /// <param name="userIsHost">bool determining whether the user is the host / lobby owner.</param>
    public void ToggleStartButtonVisibility(bool userIsHost)
    {
        _startButton.Visible = userIsHost;
    }

    /// <summary>
    /// Refresh LobbyMembersContainer. Display each lobby member's name, colour, and host icon (if applicable).
    /// </summary>
    /// <param name="lobby">Object containing lobby data.</param>
    public void UpdateLobbyMembers(Dictionary<string, object> lobby)
    {
        // Clear current lobby members list before refreshing it
        foreach(LobbyMember lobbyMember in _lobbyMembersContainer.GetChildren())
        {
            lobbyMember.QueueFree();
        }

        string lobbyOwnerCxId = (string)lobby["ownerCxId"];
        Dictionary<string, object>[] lobbyMembers = (Dictionary<string, object>[])lobby["members"];

        foreach (var lobbyMember in lobbyMembers)
        {
            string memberCxId = (string)lobbyMember["cxId"];
            string name = (string)lobbyMember["name"];
            Dictionary<string, object> extra = (Dictionary<string, object>)lobbyMember["extra"];
            int colourIndex = (int)extra["colorIndex"];
            bool userIsHost = memberCxId == lobbyOwnerCxId;

            var lobbyMemberScene = GD.Load<PackedScene>("res://Scenes/LobbyMember.tscn");
            LobbyMember newLobbyMember = (LobbyMember)lobbyMemberScene.Instantiate();

            // Add the newly instanced LobbyMember Scene to the Lobby Scene
            _lobbyMembersContainer.AddChild(newLobbyMember);

            // Set the lobby member's name, colour, and host icon
            newLobbyMember.SetName(name);
            newLobbyMember.SetColour(Main.Colours[colourIndex]);
            newLobbyMember.SetHostIcon(userIsHost);
        }
    }

    /// <summary>
    /// Change the user's colour.
    /// </summary>
    /// <param name="colourButtonIndex">int representing the colour index for GameColours.</param>
    public void OnColourButtonPressed(int colourButtonIndex)
    {
        EmitSignal(SignalName.ColourChanged, colourButtonIndex);
    }

    /// <summary>
    /// Leave lobby and return to the LobbySelect Screen.
    /// </summary>
    private void OnLeaveButtonPressed()
    {
        EmitSignal(SignalName.LeaveLobbyRequested);
    }

    /// <summary>
    /// Join a match in progress (hidden if no matches have started).
    /// </summary>
    private void OnJoinButtonPressed()
    {
        EmitSignal(SignalName.JoinMatchRequested);
    }

    /// <summary>
    /// Start a match (hidden unless user is the host / lobby owner).
    /// </summary>
    private void OnStartButtonPressed()
    {
        EmitSignal(SignalName.StartMatchRequested);
    }
}
