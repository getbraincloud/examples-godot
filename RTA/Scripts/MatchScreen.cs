using Godot;
using System;
using System.Collections.Generic;

public partial class MatchScreen : Control
{
    [Signal]
    public delegate void EndMatchRequestedEventHandler();
    [Signal]
    public delegate void LeaveMatchRequestedEventHandler();

    private VBoxContainer _lobbyMembersContainer;
    private Button _endMatchButton;
    private Button _leaveMatchButton;

    public override void _Ready()
    {
        _lobbyMembersContainer = GetNode<VBoxContainer>("MatchContainer/MatchInfo/MembersContainer");
        _endMatchButton = GetNode<Button>("MatchContainer/GameSide/MatchButtons/EndMatchButton");
        _leaveMatchButton = GetNode<Button>("MatchContainer/GameSide/MatchButtons/LeaveMatchButton");

        // Connect Button listener(s)
        _endMatchButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnEndMatchButtonPressed));
        _leaveMatchButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLeaveMatchButtonPressed));

        // Hide button(s) when not necessary
        _endMatchButton.Hide();
    }

    public void ToggleEndMatchButtonVisibility(bool userIsHost)
    {
        // TODO:  verify that this works and that we don't need to use Show() / Hide()
        _endMatchButton.Visible = userIsHost;
    }

    // TODO:  should this be the same as the lobby version?
    public void UpdateLobbyMembers(Dictionary<string, object> lobby)
    {
        // Clear current lobby members list before refreshing it
        foreach (LobbyMember lobbyMember in _lobbyMembersContainer.GetChildren())
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

            // TODO:  verify that this works as intended
            bool userIsHost = memberCxId == lobbyOwnerCxId;
            GD.Print(name + " is host: " + userIsHost);

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
    
    private void OnEndMatchButtonPressed()
    {
        // TODO
        EmitSignal(SignalName.EndMatchRequested);
    }
    
    private void OnLeaveMatchButtonPressed()
    {
        // TODO
        EmitSignal(SignalName.LeaveMatchRequested);
    }
}
