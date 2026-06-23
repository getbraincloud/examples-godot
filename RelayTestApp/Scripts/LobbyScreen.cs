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

    // Holds the dynamically built colour-swatch buttons (one per palette entry).
    private GridContainer _colourButtons;

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

    public override void _Ready()
    {
        _colourButtons = GetNode<GridContainer>("VBoxContainer/ColourButtons");
        _lobbyMembersContainer = GetNode<VBoxContainer>("VBoxContainer/LobbyMembersContainer");
        _pingDataContainer = GetNode<VBoxContainer>("VBoxContainer/PingDataContainer");
        _leaveButton = GetNode<Button>("VBoxContainer/LobbyButtons/LeaveButton");
        _joinButton = GetNode<Button>("VBoxContainer/LobbyButtons/JoinButton");
        _startButton = GetNode<Button>("VBoxContainer/LobbyButtons/StartButton");

        // Connect Button listener(s)
        _leaveButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLeaveButtonPressed));
        _joinButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnJoinButtonPressed));
        _startButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnStartButtonPressed));

        // Build one swatch per palette colour (matches the dotnet/java/react 4×10 grid).
        BuildColourButtons();

        // Hide buttons when not necessary
        _joinButton.Hide();
        _startButton.Hide();
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

        foreach (var lobbyMember in lobbyMembers)
        {
            string memberCxId = (string)lobbyMember["cxId"];
            string name = (string)lobbyMember["name"];
            Dictionary<string, object> extra = (Dictionary<string, object>)lobbyMember["extra"];

            Dictionary<string, object> pings =
                extra.ContainsKey("pings") ? extra["pings"] as Dictionary<string, object> : null;

            // Fall back to our locally captured pings for our own row (the lobby may not have
            // echoed our extra["pings"] back yet).
            if ((pings == null || pings.Count == 0) && memberCxId == _localCxId &&
                _localPings != null && _localPings.Count > 0)
            {
                pings = new Dictionary<string, object>();
                foreach (var kv in _localPings) pings[kv.Key] = kv.Value;
            }

            if (pings == null || pings.Count == 0) continue;

            var parts = new List<string>();
            foreach (var kv in pings)
            {
                int ms = Convert.ToInt32(kv.Value);
                parts.Add(kv.Key + ":" + (ms >= 999 ? "T/O" : ms.ToString()));
            }

            string text = name + (memberCxId == lobbyOwnerCxId ? " [H]" : "") + "  " + string.Join("  ", parts);
            var label = new Label { Text = text };
            label.AddThemeFontSizeOverride("font_size", 12);
            _pingDataContainer.AddChild(label);
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
