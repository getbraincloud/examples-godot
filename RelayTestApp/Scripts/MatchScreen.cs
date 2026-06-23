using Godot;
using System;
using System.Collections.Generic;

public partial class MatchScreen : Control
{
    [Signal]
    public delegate void EndMatchRequestedEventHandler();
    [Signal]
    public delegate void LeaveMatchRequestedEventHandler();

    // Settings HUD → relay send options (mirrors the dotnet RelayTestApp).
    [Signal]
    public delegate void SendReliableChangedEventHandler(bool value);
    [Signal]
    public delegate void SendOrderedChangedEventHandler(bool value);
    [Signal]
    public delegate void SendChannelChangedEventHandler(int value);
    [Signal]
    public delegate void SendMaskChangedEventHandler(string cxId, bool value);

    private VBoxContainer _lobbyMembersContainer;
    private Button _endMatchButton;
    private Button _leaveMatchButton;

    // Settings HUD controls
    private VBoxContainer _playersMaskContainer;
    private CheckBox _reliableCheck;
    private CheckBox _orderedCheck;
    private OptionButton _channelOption;

    // Shared per-member send mask (cxId -> allowed), owned by Main; read here for display.
    private Dictionary<string, bool> _allowSendTo;
    private string _localCxId;

    // Per-member live-ping labels in the Players panel (cxId -> Label), updated by SetActivePing.
    private readonly Dictionary<string, Label> _pingLabels = new Dictionary<string, Label>();

    public override void _Ready()
    {
        _lobbyMembersContainer = GetNode<VBoxContainer>("MatchContainer/MatchInfo/MembersContainer");
        _endMatchButton = GetNode<Button>("MatchContainer/GameSide/MatchButtons/EndMatchButton");
        _leaveMatchButton = GetNode<Button>("MatchContainer/GameSide/MatchButtons/LeaveMatchButton");

        _playersMaskContainer = GetNode<VBoxContainer>("MatchContainer/MatchInfo/PlayersMaskContainer");
        _reliableCheck = GetNode<CheckBox>("MatchContainer/MatchInfo/ReliableCheck");
        _orderedCheck = GetNode<CheckBox>("MatchContainer/MatchInfo/OrderedCheck");
        _channelOption = GetNode<OptionButton>("MatchContainer/MatchInfo/ChannelOption");

        // Channel options map directly to BrainCloudRelay channel ints 0–3.
        if (_channelOption.ItemCount == 0)
        {
            _channelOption.AddItem("High Priority 1", 0);
            _channelOption.AddItem("High Priority 2", 1);
            _channelOption.AddItem("Normal Priority", 2);
            _channelOption.AddItem("Low Priority", 3);
        }

        // Connect Button listener(s)
        _endMatchButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnEndMatchButtonPressed));
        _leaveMatchButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLeaveMatchButtonPressed));

        // Connect Settings HUD listener(s) — re-emit to Main.
        _reliableCheck.Connect(CheckBox.SignalName.Toggled, new Callable(this, MethodName.OnReliableToggled));
        _orderedCheck.Connect(CheckBox.SignalName.Toggled, new Callable(this, MethodName.OnOrderedToggled));
        _channelOption.Connect(OptionButton.SignalName.ItemSelected, new Callable(this, MethodName.OnChannelSelected));

        // Hide button(s) when not necessary
        _endMatchButton.Hide();
    }

    public void ToggleEndMatchButtonVisibility(bool userIsHost)
    {
        _endMatchButton.Visible = userIsHost;
    }

    /// <summary>
    /// Provide the shared send-mask state (owned by Main) + our own cxId so the Players mask can
    /// render the current checked state and mark ourselves. Call before UpdateLobbyMembers.
    /// </summary>
    public void SetSendMaskState(Dictionary<string, bool> allowSendTo, string localCxId)
    {
        _allowSendTo = allowSendTo;
        _localCxId = localCxId;
    }

    /// <summary>
    /// Initialize the reliability/order/channel controls to Main's current values (without
    /// emitting change signals).
    /// </summary>
    public void SetSendOptions(bool reliable, bool ordered, int channel)
    {
        if (_reliableCheck == null) return; // not yet _Ready
        _reliableCheck.SetPressedNoSignal(reliable);
        _orderedCheck.SetPressedNoSignal(ordered);
        _channelOption.Select(Math.Clamp(channel, 0, _channelOption.ItemCount - 1));
    }

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

        BuildPlayersMask(lobbyOwnerCxId, lobbyMembers);
    }

    /// <summary>
    /// Build the per-player send-mask checkboxes (which players a shockwave is relayed to). The
    /// checked state reflects the shared mask owned by Main; toggling re-emits to Main. Mirrors
    /// the dotnet "Players" panel — name in the member colour, with [H] host / (me) self markers.
    /// </summary>
    private void BuildPlayersMask(string lobbyOwnerCxId, Dictionary<string, object>[] lobbyMembers)
    {
        foreach (Node child in _playersMaskContainer.GetChildren())
        {
            child.QueueFree();
        }
        _pingLabels.Clear();

        foreach (var lobbyMember in lobbyMembers)
        {
            string memberCxId = (string)lobbyMember["cxId"];
            string name = (string)lobbyMember["name"];
            Dictionary<string, object> extra = (Dictionary<string, object>)lobbyMember["extra"];
            int colourIndex = (int)extra["colorIndex"];

            string label = name
                + (memberCxId == lobbyOwnerCxId ? " [H]" : "")
                + (memberCxId == _localCxId ? " (me)" : "");

            bool allowed = _allowSendTo != null && _allowSendTo.TryGetValue(memberCxId, out bool a) && a;

            // Row = mask checkbox + live-ping label (mirrors the dotnet player row).
            var row = new HBoxContainer();

            var cb = new CheckBox { Text = label };
            cb.SetPressedNoSignal(allowed);
            cb.AddThemeColorOverride("font_color", Main.Colours[colourIndex]);

            string capturedCxId = memberCxId;
            cb.Toggled += pressed => EmitSignal(SignalName.SendMaskChanged, capturedCxId, pressed);

            var ping = new Label { Text = "..." };
            ping.AddThemeFontSizeOverride("font_size", 11);

            row.AddChild(cb);
            row.AddChild(ping);
            _playersMaskContainer.AddChild(row);
            _pingLabels[memberCxId] = ping;
        }
    }

    /// <summary>
    /// Update a member's live relay-ping label (called by Main on each relay_ping broadcast).
    /// </summary>
    public void SetActivePing(string cxId, string text)
    {
        if (_pingLabels.TryGetValue(cxId, out var label)) label.Text = text;
    }

    private void OnEndMatchButtonPressed()
    {
        EmitSignal(SignalName.EndMatchRequested);
    }

    private void OnLeaveMatchButtonPressed()
    {
        EmitSignal(SignalName.LeaveMatchRequested);
    }

    private void OnReliableToggled(bool pressed) => EmitSignal(SignalName.SendReliableChanged, pressed);
    private void OnOrderedToggled(bool pressed) => EmitSignal(SignalName.SendOrderedChanged, pressed);
    private void OnChannelSelected(long index) => EmitSignal(SignalName.SendChannelChanged, (int)index);
}
