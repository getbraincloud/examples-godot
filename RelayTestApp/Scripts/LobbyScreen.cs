using BrainCloud;
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
    [Signal]
    public delegate void ReadyToggleRequestedEventHandler();
    [Signal]
    public delegate void LobbySignalSendRequestedEventHandler(string text);

    // Holds the dynamically built colour-swatch buttons (one per palette entry) — shown in a
    // popup anchored to your own member row (cpp: ImGui popup from your row; react:
    // colorPickerOpen toggled from your row), not an always-visible grid.
    private GridContainer _colourButtons;
    private PopupPanel _colourPopup;

    // Lobby id + region + player-count header, shown above the colour grid (matches GDScript).
    private Label _lobbyInfoLabel;

    // Holds/Displays members in lobby
    private VBoxContainer _lobbyMembersContainer;

    // Holds one row per member showing their region ping data (when ping data was gathered).
    private VBoxContainer _pingDataContainer;

    // This client's own region pings + cxId, so our row renders before the lobby echoes
    // our extra["pings"] back to us.
    private Dictionary<string, int> _localPings;
    private string _localCxId;

    // Leave lobby (return to LobbySelect Screen)
    private Button _leaveButton;

    // Join a match that is already in progress (TODO:  requires JoinInProgress to be enabled in brainCloud portal)
    private Button _joinButton;

    // Start a match (visible ONLY to host / lobby owner)
    private Button _startButton;

    // Ready-up toggle (visible ONLY to non-host members — only the host has a "Start"
    // button, but everyone else still needs a way to signal they're ready before the
    // host starts).
    private Button _readyButton;

    // This-lobby chat, global chat, and the leaderboard viewer — tabbed (CHAT/LEADERBOARDS/
    // INFO), only one visible at a time; showing all three simultaneously is what caused them
    // to visually overlap. Chat itself has a THIS LOBBY/GLOBAL sub-tab.
    private VBoxContainer _chatTab;
    private VBoxContainer _leaderboardsTab;
    private VBoxContainer _infoTab;
    private Button _chatTabBtn;
    private Button _leaderboardsTabBtn;
    private Button _infoTabBtn;
    private Button _thisLobbyTabBtn;
    private Button _globalTabBtn;
    private LobbySignalChatPanel _lobbySignalChatPanel;
    private GlobalChatPanel _globalChatPanel;
    private LeaderboardPanel _leaderboardPanel;

    // Small non-blocking status line shown while a round is being provisioned (STARTING ->
    // ROOM_READY) — the rest of this screen (chat, member list, etc.) stays fully usable
    // underneath it instead of being replaced by a blocking loading screen.
    private Label _provisioningBanner;

    public override void _Ready()
    {
        const string leftCard = "CenterContainer/HBoxContainer/LeftCard/VBoxContainer";
        const string rightCard = "CenterContainer/HBoxContainer/RightPanel/RightCard";
        const string rightPanel = "CenterContainer/HBoxContainer/RightPanel";

        _colourButtons = GetNode<GridContainer>("ColorPopup/ColourButtons");
        _colourPopup = GetNode<PopupPanel>("ColorPopup");
        _lobbyMembersContainer = GetNode<VBoxContainer>($"{leftCard}/MembersScroll/LobbyMembersContainer");
        _leaveButton = GetNode<Button>($"{leftCard}/LobbyButtons/LeaveButton");
        _joinButton = GetNode<Button>($"{leftCard}/LobbyButtons/ActionButtons/JoinButton");
        _startButton = GetNode<Button>($"{leftCard}/LobbyButtons/ActionButtons/StartButton");
        _readyButton = GetNode<Button>($"{leftCard}/LobbyButtons/ActionButtons/ReadyButton");
        _chatTab = GetNode<VBoxContainer>($"{rightCard}/ChatTab");
        _leaderboardsTab = GetNode<VBoxContainer>($"{rightCard}/LeaderboardsTab");
        _infoTab = GetNode<VBoxContainer>($"{rightCard}/InfoTab");
        _pingDataContainer = GetNode<VBoxContainer>($"{rightCard}/InfoTab/PingDataContainer");
        _chatTabBtn = GetNode<Button>($"{rightPanel}/TabRow/ChatTabBtn");
        _leaderboardsTabBtn = GetNode<Button>($"{rightPanel}/TabRow/LeaderboardsTabBtn");
        _infoTabBtn = GetNode<Button>($"{rightPanel}/TabRow/InfoTabBtn");
        _thisLobbyTabBtn = GetNode<Button>($"{rightCard}/ChatTab/SubTabRow/ThisLobbyTabBtn");
        _globalTabBtn = GetNode<Button>($"{rightCard}/ChatTab/SubTabRow/GlobalTabBtn");

        // Connect Button listener(s)
        _leaveButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLeaveButtonPressed));
        _joinButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnJoinButtonPressed));
        _startButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnStartButtonPressed));
        _readyButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnReadyButtonPressed));
        _chatTabBtn.Connect(Button.SignalName.Pressed, Callable.From(() => SetRightTab("chat")));
        _leaderboardsTabBtn.Connect(Button.SignalName.Pressed, Callable.From(() => SetRightTab("leaderboards")));
        _infoTabBtn.Connect(Button.SignalName.Pressed, Callable.From(() => SetRightTab("info")));
        _thisLobbyTabBtn.Connect(Button.SignalName.Pressed, Callable.From(() => SetChatSubTab("thisLobby")));
        _globalTabBtn.Connect(Button.SignalName.Pressed, Callable.From(() => SetChatSubTab("global")));
        SetRightTab("chat");
        SetChatSubTab("thisLobby");

        // Build one swatch per palette colour (matches the dotnet/java/react 4×10 grid).
        BuildColourButtons();

        // Hide buttons when not necessary
        _joinButton.Hide();
        _startButton.Hide();
        _readyButton.Hide();

        // Lobby id / region / player-count header, prepended above the colour grid so the
        // C# lobby shows the same context info as the GDScript client.
        _lobbyInfoLabel = new Label();
        _lobbyInfoLabel.AddThemeFontSizeOverride("font_size", 14);
        var vbox = GetNode<VBoxContainer>(leftCard);
        vbox.AddChild(_lobbyInfoLabel);
        vbox.MoveChild(_lobbyInfoLabel, 0);

        _provisioningBanner = new Label();
        _provisioningBanner.AddThemeFontSizeOverride("font_size", 13);
        _provisioningBanner.AddThemeColorOverride("font_color", new Color(1f, 0.85f, 0.4f));
        _provisioningBanner.Hide();
        vbox.AddChild(_provisioningBanner);
        vbox.MoveChild(_provisioningBanner, 0);
    }

    /// <summary>
    /// Show/hide the non-blocking "starting the next round" status line.
    /// </summary>
    public void SetProvisioning(bool isProvisioning, string status)
    {
        _provisioningBanner.Text = "Starting round... " + status;
        _provisioningBanner.Visible = isProvisioning;
    }

    /// <summary>
    /// Create a colour-swatch button for every entry in the (server- or default-) palette,
    /// sized to Main.Colours so all 40 (or however many) colours are selectable — never a
    /// hardcoded subset. Mirrors the other CursorParty clients so colour indices line up.
    /// </summary>
    private void BuildColourButtons()
    {
        foreach (Node child in _colourButtons.GetChildren())
        {
            child.QueueFree();
        }

        for (int i = 0; i < Main.Colours.Length; i++)
        {
            int colourIndex = i;
            Color colour = Main.Colours[i];

            var swatch = new Button
            {
                CustomMinimumSize = new Vector2(44, 44),
            };

            var normalStyleBox = new StyleBoxFlat
            {
                BgColor = colour,
                BorderColor = colour,
                BorderWidthBottom = 3,
                BorderWidthLeft = 3,
                BorderWidthRight = 3,
                BorderWidthTop = 3,
            };
            var hoverStyleBox = (StyleBoxFlat)normalStyleBox.Duplicate();
            hoverStyleBox.BgColor = new Color("#1F2124");

            swatch.AddThemeStyleboxOverride("normal", normalStyleBox);
            swatch.AddThemeStyleboxOverride("hover", hoverStyleBox);
            swatch.Pressed += () => OnColourButtonPressed(colourIndex);

            _colourButtons.AddChild(swatch);
        }
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
        _readyButton.Visible = !userIsHost;
    }

    /// <summary>
    /// Updates the ready-toggle button's label to reflect this user's current ready state.
    /// </summary>
    public void SetReadyButtonState(bool isReady)
    {
        _readyButton.Text = isReady ? "NOT READY" : "READY UP";
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

        // Header: lobby id, region (id prefix), and current player count.
        string lobbyId = lobby.ContainsKey("id") ? lobby["id"]?.ToString() ?? "" : "";
        string region = lobbyId.Contains(":") ? lobbyId.Split(':')[0] : lobbyId;
        _lobbyInfoLabel.Text = $"Lobby: {lobbyId}\nRegion: {region}\nPlayers: {lobbyMembers.Length}";

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
            newLobbyMember.Connect(LobbyMember.SignalName.ColourSwatchClicked, Callable.From(() => OnColourSwatchClicked(newLobbyMember)));
        }

        UpdatePingData(lobbyOwnerCxId, lobbyMembers);
    }

    /// <summary>
    /// Provide this client's own region ping data + cxId so our ping row can render before the
    /// lobby echoes our shared extra["pings"] back to us. Call before UpdateLobbyMembers.
    /// </summary>
    public void SetLocalPingData(Dictionary<string, int> pings, string cxId)
    {
        _localPings = pings;
        _localCxId = cxId;
    }

    /// <summary>
    /// Render one row per lobby member showing the region ping times they shared via lobby
    /// extra["pings"] (gathered from PingRegions before matchmaking). Mirrors the dotnet/java/
    /// react CursorParty clients so every member sees everyone's ping data. Members that shared
    /// no ping data (ping data disabled) are skipped.
    /// </summary>
    private void UpdatePingData(string lobbyOwnerCxId, Dictionary<string, object>[] lobbyMembers)
    {
        foreach (Node child in _pingDataContainer.GetChildren())
        {
            child.QueueFree();
        }

        // Pings for a member: their shared extra["pings"], falling back to our local capture
        // for our own row (the lobby may not have echoed it back yet).
        Dictionary<string, object> PingsFor(Dictionary<string, object> member)
        {
            string cxId = (string)member["cxId"];
            var extra = (Dictionary<string, object>)member["extra"];
            var p = extra.ContainsKey("pings") ? extra["pings"] as Dictionary<string, object> : null;
            if ((p == null || p.Count == 0) && cxId == _localCxId && _localPings != null && _localPings.Count > 0)
            {
                p = new Dictionary<string, object>();
                foreach (var kv in _localPings) p[kv.Key] = kv.Value;
            }
            return p;
        }

        // Union of every region any member reported, sorted, so all rows share the same columns.
        var regions = new List<string>();
        foreach (var m in lobbyMembers)
        {
            var p = PingsFor(m);
            if (p != null) foreach (var r in p.Keys) if (!regions.Contains(r)) regions.Add(r);
        }
        if (regions.Count == 0) return;
        regions.Sort();

        var title = new Label { Text = "Ping Data (ms)" };
        title.AddThemeFontSizeOverride("font_size", 13);
        _pingDataContainer.AddChild(title);

        // Header row: empty name cell + one column per region.
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 8);
        var headerName = new Label();
        headerName.CustomMinimumSize = new Vector2(120, 0);
        header.AddChild(headerName);
        foreach (var r in regions)
        {
            var lbl = new Label { Text = r };
            lbl.AddThemeFontSizeOverride("font_size", 11);
            lbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.7f));
            lbl.CustomMinimumSize = new Vector2(80, 0);
            header.AddChild(lbl);
        }
        _pingDataContainer.AddChild(header);

        // One row per member (ALL members; "—" for a region they didn't report), name tinted by
        // their colour, "T/O" at >=999 — mirrors the GDScript client's table exactly.
        foreach (var m in lobbyMembers)
        {
            string memberCxId = (string)m["cxId"];
            string name = (string)m["name"];
            var extra = (Dictionary<string, object>)m["extra"];
            int colourIndex = extra.ContainsKey("colorIndex") ? Convert.ToInt32(extra["colorIndex"]) : 0;
            var pings = PingsFor(m);

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);
            var nameLbl = new Label { Text = name + (memberCxId == lobbyOwnerCxId ? " [H]" : "") };
            nameLbl.AddThemeFontSizeOverride("font_size", 11);
            nameLbl.CustomMinimumSize = new Vector2(120, 0);
            if (colourIndex >= 0 && colourIndex < Main.Colours.Length)
                nameLbl.AddThemeColorOverride("font_color", Main.Colours[colourIndex]);
            row.AddChild(nameLbl);

            foreach (var r in regions)
            {
                var cell = new Label();
                cell.AddThemeFontSizeOverride("font_size", 11);
                cell.CustomMinimumSize = new Vector2(80, 0);
                if (pings != null && pings.ContainsKey(r))
                {
                    int ms = Convert.ToInt32(pings[r]);
                    cell.Text = ms >= 999 ? "T/O" : ms + " ms";
                    cell.AddThemeColorOverride("font_color",
                        ms < 100 ? new Color(0.3f, 1f, 0.4f)
                        : ms < 250 ? new Color(1f, 1f, 0.3f)
                        : new Color(1f, 0.4f, 0.4f));
                }
                else
                {
                    cell.Text = "—";
                    cell.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.5f));
                }
                row.AddChild(cell);
            }
            _pingDataContainer.AddChild(row);
        }
    }

    /// <summary>
    /// Instance the this-lobby chat, global chat, and leaderboard panels. Called once by Main
    /// right after this screen is shown.
    /// </summary>
    public void InitExtraPanels(BrainCloudWrapper bc)
    {
        var signalChatScene = GD.Load<PackedScene>("res://Scenes/LobbySignalChatPanel.tscn");
        _lobbySignalChatPanel = (LobbySignalChatPanel)signalChatScene.Instantiate();
        _chatTab.AddChild(_lobbySignalChatPanel);
        _lobbySignalChatPanel.Connect(LobbySignalChatPanel.SignalName.SendRequested, new Callable(this, MethodName.OnLobbySignalSendRequested));

        var chatScene = GD.Load<PackedScene>("res://Scenes/GlobalChatPanel.tscn");
        _globalChatPanel = (GlobalChatPanel)chatScene.Instantiate();
        _chatTab.AddChild(_globalChatPanel);
        _globalChatPanel.Init(bc);

        var leaderboardScene = GD.Load<PackedScene>("res://Scenes/LeaderboardPanel.tscn");
        _leaderboardPanel = (LeaderboardPanel)leaderboardScene.Instantiate();
        _leaderboardsTab.AddChild(_leaderboardPanel);
        _leaderboardPanel.Init(bc);

        SetChatSubTab(_chatSubTab);
    }

    private string _rightTab = "chat";
    private string _chatSubTab = "thisLobby";

    private void SetRightTab(string tab)
    {
        _rightTab = tab;
        _chatTab.Visible = tab == "chat";
        _leaderboardsTab.Visible = tab == "leaderboards";
        _infoTab.Visible = tab == "info";
        _chatTabBtn.Modulate = tab == "chat" ? new Color(1.4f, 1.4f, 1.4f) : Colors.White;
        _leaderboardsTabBtn.Modulate = tab == "leaderboards" ? new Color(1.4f, 1.4f, 1.4f) : Colors.White;
        _infoTabBtn.Modulate = tab == "info" ? new Color(1.4f, 1.4f, 1.4f) : Colors.White;
    }

    private void SetChatSubTab(string tab)
    {
        _chatSubTab = tab;
        if (_lobbySignalChatPanel != null) _lobbySignalChatPanel.Visible = tab == "thisLobby";
        if (_globalChatPanel != null) _globalChatPanel.Visible = tab == "global";
        _thisLobbyTabBtn.Modulate = tab == "thisLobby" ? new Color(1.4f, 1.4f, 1.4f) : Colors.White;
        _globalTabBtn.Modulate = tab == "global" ? new Color(1.4f, 1.4f, 1.4f) : Colors.White;
    }

    /// <summary>
    /// Replay this-lobby chat history into a freshly (re)created screen — Main owns the
    /// history so it survives across a return-to-lobby-after-a-match cycle, since this whole
    /// screen is recreated on every GoToLobbyScreen() call.
    /// </summary>
    public void SetLobbyChatHistory(IEnumerable<(string fromName, string text, bool isMe)> history)
    {
        _lobbySignalChatPanel.Clear();
        foreach (var m in history) _lobbySignalChatPanel.AddMessage(m.fromName, m.text, m.isMe);
    }

    public void AddLobbyChatMessage(string fromName, string text, bool isMe)
    {
        _lobbySignalChatPanel?.AddMessage(fromName, text, isMe);
    }

    private void OnLobbySignalSendRequested(string text)
    {
        EmitSignal(SignalName.LobbySignalSendRequested, text);
    }

    /// <summary>
    /// Change the user's colour.
    /// </summary>
    /// <param name="colourButtonIndex">int representing the colour index for GameColours.</param>
    public void OnColourButtonPressed(int colourButtonIndex)
    {
        _colourPopup.Hide();
        EmitSignal(SignalName.ColourChanged, colourButtonIndex);
    }

    /// <summary>
    /// Opens the colour-swatch popup anchored just below the given row (your own row only —
    /// LobbyMember gates ColourSwatchClicked to the isMe row via its disabled ColorButton).
    /// </summary>
    private void OnColourSwatchClicked(LobbyMember row)
    {
        var rect = row.GetGlobalRect();
        _colourPopup.Position = new Vector2I((int)rect.Position.X, (int)(rect.Position.Y + rect.Size.Y));
        _colourPopup.Popup();
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

    private void OnReadyButtonPressed()
    {
        EmitSignal(SignalName.ReadyToggleRequested);
    }
}
