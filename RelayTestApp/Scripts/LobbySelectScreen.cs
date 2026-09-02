using BrainCloud;
using Godot;
using System;

public partial class LobbySelectScreen : Control
{
    [Signal]
    public delegate void LogOutRequestedEventHandler();

    [Signal]
    public delegate void MatchMakingRequestedEventHandler(string lobbyType, string protocol, bool usePingData);

    // Display this user's name in greeting message once authenticated
    private Label _nameLabel;

    // Options Button containing lobby types configured for this app in the brainCloud portal
    private OptionButton _lobbyOptions;

    // Options Button for the relay transport protocol (WEBSOCKET / TCP / UDP)
    private OptionButton _protocolOptions;

    // Toggle: ping regions before matchmaking to pick the lowest-latency region
    private CheckBox _usePingDataCheck;

    // Display error messages relating to matchmaking
    private Label _errorMessageLabel;

    // Attempt to find or create a lobby
    private Button _matchMakeButton;

    // Log out of brainCloud and return to Authentication Screen
    private Button _logOutButton;

    // Global chat + leaderboard viewer — tabbed (LEADERBOARD/CHAT), only one visible at a
    // time; showing both simultaneously side by side is what caused them to visually overlap.
    private PanelContainer _extraPanels;
    private Button _leaderboardTabBtn;
    private Button _chatTabBtn;
    private GlobalChatPanel _chatPanel;
    private LeaderboardPanel _leaderboardPanel;

    public override void _Ready()
    {
        const string leftCard = "CenterContainer/HBoxContainer/LeftCard/VBoxContainer";
        const string rightPanel = "CenterContainer/HBoxContainer/RightPanel";
        _nameLabel = GetNode<Label>($"{leftCard}/AuthenticatedUser/NameLabel");
        _lobbyOptions = GetNode<OptionButton>($"{leftCard}/LobbyOptions");
        _protocolOptions = GetNode<OptionButton>($"{leftCard}/ProtocolOptions");
        _usePingDataCheck = GetNode<CheckBox>($"{leftCard}/PingDataCheck");
        _errorMessageLabel = GetNode<Label>($"{leftCard}/ErrorMessage");
        _matchMakeButton = GetNode<Button>($"{leftCard}/MenuButtons/MatchMakeButton");
        _logOutButton = GetNode<Button>($"{leftCard}/MenuButtons/LogOutButton");
        _extraPanels = GetNode<PanelContainer>($"{rightPanel}/ExtraPanels");
        _leaderboardTabBtn = GetNode<Button>($"{rightPanel}/TabRow/LeaderboardTabBtn");
        _chatTabBtn = GetNode<Button>($"{rightPanel}/TabRow/ChatTabBtn");

        // Connect Button listener(s)
        _logOutButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLogOutButtonPressed));
        _matchMakeButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnMatchMakeButtonPressed));
        _leaderboardTabBtn.Connect(Button.SignalName.Pressed, Callable.From(() => SetRightTab("leaderboard")));
        _chatTabBtn.Connect(Button.SignalName.Pressed, Callable.From(() => SetRightTab("chat")));

        // Hide error message when not necessary
        _errorMessageLabel.Hide();
    }

    private string _rightTab = "leaderboard";

    private void SetRightTab(string tab)
    {
        _rightTab = tab;
        if (_leaderboardPanel != null) _leaderboardPanel.Visible = tab == "leaderboard";
        if (_chatPanel != null) _chatPanel.Visible = tab == "chat";
        _leaderboardTabBtn.Modulate = tab == "leaderboard" ? new Color(1.4f, 1.4f, 1.4f) : Colors.White;
        _chatTabBtn.Modulate = tab == "chat" ? new Color(1.4f, 1.4f, 1.4f) : Colors.White;
    }

    /// <summary>
    /// Display this user's name in greeting message once authenticated.
    /// </summary>
    /// <param name="name">string value of this user's name.</param>
    public void SetNameLabel(string name)
    {
        _nameLabel.Text = name;
    }

    /// <summary>
    /// Instance the global chat + leaderboard panels and hand them the brainCloud wrapper.
    /// Called once by Main right after this screen is shown. Chat requires RTT — Main enables
    /// it as soon as this screen is reached, so the panel just waits/retries until it's up.
    /// </summary>
    public void InitExtraPanels(BrainCloudWrapper bc)
    {
        var chatScene = GD.Load<PackedScene>("res://Scenes/GlobalChatPanel.tscn");
        _chatPanel = (GlobalChatPanel)chatScene.Instantiate();
        _extraPanels.AddChild(_chatPanel);
        _chatPanel.Init(bc);

        var leaderboardScene = GD.Load<PackedScene>("res://Scenes/LeaderboardPanel.tscn");
        _leaderboardPanel = (LeaderboardPanel)leaderboardScene.Instantiate();
        _extraPanels.AddChild(_leaderboardPanel);
        _leaderboardPanel.Init(bc);

        SetRightTab(_rightTab);
    }

    /// <summary>
    /// Populate the lobby-type dropdown from the app-configured list (AllLobbyTypes global
    /// property). If the list is empty (not loaded yet) the scene's default items are kept.
    /// </summary>
    public void SetLobbyTypes(System.Collections.Generic.List<string> lobbyTypes)
    {
        if (lobbyTypes == null || lobbyTypes.Count == 0) return;

        _lobbyOptions.Clear();
        foreach (string lobbyType in lobbyTypes)
        {
            _lobbyOptions.AddItem(lobbyType);
        }
        _lobbyOptions.Selected = 0;
    }

    /// <summary>
    /// Display an error message.
    /// </summary>
    /// <param name="errorMsg"></param>
    public void SetErrorMessage(string errorMsg)
    {
        _errorMessageLabel.Text = errorMsg;
        _errorMessageLabel.Show();
    }

    /// <summary>
    /// Attempt log out.
    /// </summary>
    private void OnLogOutButtonPressed()
    {
        EmitSignal(SignalName.LogOutRequested);
    }

    /// <summary>
    /// Attempt matchmaking.
    /// </summary>
    private void OnMatchMakeButtonPressed()
    {
        // Hide error message on retry
        _errorMessageLabel.Hide();

        string lobbyType = _lobbyOptions.Text;

        if(string.IsNullOrEmpty(lobbyType))
        {
            SetErrorMessage("Select a lobby type");

            return;
        }

        // Relay transport protocol (defaults to WEBSOCKET if nothing selected)
        string protocol = string.IsNullOrEmpty(_protocolOptions.Text) ? "WEBSOCKET" : _protocolOptions.Text;

        // Attempt to find or create a lobby
        EmitSignal(SignalName.MatchMakingRequested, lobbyType, protocol, _usePingDataCheck.ButtonPressed);
    }
}
