using Godot;
using System;
using System.Collections.Generic;

public partial class MatchScreen : Control
{
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
    private VBoxContainer _scoreboardContainer;
    private Button _leaveMatchButton;

    // Top-right HUD — match countdown + this client's own live relay ping.
    private Label _timerLabel;
    private Label _myPingLabel;

    // Cached so UpdateScoreboard can resolve each entry's name/colour by cxId without Main
    // having to pass the full lobby dict along with every ~250ms coverage recompute.
    private Dictionary<string, object>[] _lastLobbyMembers = new Dictionary<string, object>[0];

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
        const string matchInfo = "CenterContainer/MatchContainer/LeftCard/MatchInfo";

        _lobbyMembersContainer = GetNode<VBoxContainer>($"{matchInfo}/MembersContainer");
        _scoreboardContainer = GetNode<VBoxContainer>($"{matchInfo}/Scoreboard");
        _leaveMatchButton = GetNode<Button>("HUD/TopRightCard/VBoxContainer/LeaveMatchButton");
        _timerLabel = GetNode<Label>("HUD/TopRightCard/VBoxContainer/TimerRow/TimerLabel");
        _myPingLabel = GetNode<Label>("HUD/TopRightCard/VBoxContainer/PingLabel");

        _playersMaskContainer = GetNode<VBoxContainer>($"{matchInfo}/PlayersMaskContainer");
        _reliableCheck = GetNode<CheckBox>($"{matchInfo}/ReliableCheck");
        _orderedCheck = GetNode<CheckBox>($"{matchInfo}/OrderedCheck");
        _channelOption = GetNode<OptionButton>($"{matchInfo}/ChannelOption");

        // Channel options map directly to BrainCloudRelay channel ints 0–3.
        if (_channelOption.ItemCount == 0)
        {
            _channelOption.AddItem("High Priority 1", 0);
            _channelOption.AddItem("High Priority 2", 1);
            _channelOption.AddItem("Normal Priority", 2);
            _channelOption.AddItem("Low Priority", 3);
        }

        // Connect Button listener(s)
        _leaveMatchButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLeaveMatchButtonPressed));

        // Connect Settings HUD listener(s) — re-emit to Main.
        _reliableCheck.Connect(CheckBox.SignalName.Toggled, new Callable(this, MethodName.OnReliableToggled));
        _orderedCheck.Connect(CheckBox.SignalName.Toggled, new Callable(this, MethodName.OnOrderedToggled));
        _channelOption.Connect(OptionButton.SignalName.ItemSelected, new Callable(this, MethodName.OnChannelSelected));
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
        _lastLobbyMembers = lobbyMembers;

        foreach (var lobbyMember in lobbyMembers)
        {
            string memberCxId = (string)lobbyMember["cxId"];
            string name = (string)lobbyMember["name"];
            Dictionary<string, object> extra = (Dictionary<string, object>)lobbyMember["extra"];
            int colourIndex = (int)extra["colorIndex"];

            bool userIsHost = memberCxId == lobbyOwnerCxId;
            bool isReady = lobbyMember.ContainsKey("isReady") && Convert.ToBoolean(lobbyMember["isReady"]);

            var lobbyMemberScene = GD.Load<PackedScene>("res://Scenes/LobbyMember.tscn");
            LobbyMember newLobbyMember = (LobbyMember)lobbyMemberScene.Instantiate();

            // Add the newly instanced LobbyMember Scene to the Lobby Scene
            _lobbyMembersContainer.AddChild(newLobbyMember);

            // Set the lobby member's name, colour, host icon, YOU badge, and ready state
            newLobbyMember.SetName(name);
            newLobbyMember.SetColour(Main.Colours[colourIndex]);
            newLobbyMember.SetIsHost(userIsHost);
            newLobbyMember.SetCxId(memberCxId);
            newLobbyMember.SetIsMe(memberCxId == _localCxId);
            newLobbyMember.SetReady(isReady);
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
        if (cxId == _localCxId) _myPingLabel.Text = "● Ping: " + text;
    }

    /// <summary>
    /// Updates the top-right HUD countdown (called by Main.TickMatch roughly every 250ms).
    /// </summary>
    public void UpdateTimer(long remainingMs)
    {
        long remainingSec = remainingMs / 1000;
        _timerLabel.Text = $"{remainingSec / 60:D2}:{remainingSec % 60:D2}";
    }

    /// <summary>
    /// Live RANK/PLAYER/COVERAGE standings — see Coverage.cs (Main.TickMatch recomputes this
    /// roughly every 250ms from the current splotches).
    /// </summary>
    public void UpdateScoreboard(List<Coverage.Entry> coverage, string localCxId)
    {
        foreach (Node child in _scoreboardContainer.GetChildren()) child.QueueFree();

        foreach (var entry in coverage)
        {
            Dictionary<string, object> member = null;
            foreach (var m in _lastLobbyMembers)
            {
                if ((string)m["cxId"] == entry.CxId) { member = m; break; }
            }
            if (member == null) continue;

            bool isMe = entry.CxId == localCxId;
            var extra = (Dictionary<string, object>)member["extra"];
            int colourIndex = extra.ContainsKey("colorIndex") ? Convert.ToInt32(extra["colorIndex"]) : 0;

            var row = new HBoxContainer();

            var rank = new Label { Text = "#" + entry.Rank, CustomMinimumSize = new Vector2(36, 0) };
            rank.AddThemeColorOverride("font_color", LeaderboardPanel.RankColor(entry.Rank));

            var dot = new ColorRect { CustomMinimumSize = new Vector2(10, 10), Color = Main.Colours[colourIndex % Main.Colours.Length] };

            var name = new Label { Text = (string)member["name"] + (isMe ? " (YOU)" : "") };
            name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            if (isMe) name.AddThemeColorOverride("font_color", new Color(0.35f, 1f, 0.45f));

            var coveragePct = new Label { Text = entry.CoveragePct.ToString("F0") + "%" };
            if (isMe) coveragePct.AddThemeColorOverride("font_color", new Color(0.35f, 1f, 0.45f));

            row.AddChild(rank);
            row.AddChild(dot);
            row.AddChild(name);
            row.AddChild(coveragePct);
            _scoreboardContainer.AddChild(row);
        }
    }

    private void OnLeaveMatchButtonPressed()
    {
        EmitSignal(SignalName.LeaveMatchRequested);
    }

    private void OnReliableToggled(bool pressed) => EmitSignal(SignalName.SendReliableChanged, pressed);
    private void OnOrderedToggled(bool pressed) => EmitSignal(SignalName.SendOrderedChanged, pressed);
    private void OnChannelSelected(long index) => EmitSignal(SignalName.SendChannelChanged, (int)index);
}
