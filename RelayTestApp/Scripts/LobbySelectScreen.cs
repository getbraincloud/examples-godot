using Godot;
using System;

public partial class LobbySelectScreen : Control
{
    [Signal]
    public delegate void LogOutRequestedEventHandler();

    [Signal]
    public delegate void MatchMakingRequestedEventHandler(string lobbyType);

    // Display this user's name in greeting message once authenticated
    private Label _nameLabel;

    // Options Button containing lobby types configured for this app in the brainCloud portal
    private OptionButton _lobbyOptions;

    // Display error messages relating to matchmaking
    private Label _errorMessageLabel;

    // Attempt to find or create a lobby
    private Button _matchMakeButton;

    // Log out of brainCloud and return to Authentication Screen
    private Button _logOutButton;

    public override void _Ready()
    {
        _nameLabel = GetNode<Label>("VBoxContainer/AuthenticatedUser/NameLabel");
        _lobbyOptions = GetNode<OptionButton>("VBoxContainer/LobbyOptions");
        _errorMessageLabel = GetNode<Label>("VBoxContainer/ErrorMessage");
        _matchMakeButton = GetNode<Button>("VBoxContainer/MenuButtons/MatchMakeButton");
        _logOutButton = GetNode<Button>("VBoxContainer/MenuButtons/LogOutButton");

        // Connect Button listener(s)
        _logOutButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnLogOutButtonPressed));
        _matchMakeButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnMatchMakeButtonPressed));

        // Hide error message when not necessary
        _errorMessageLabel.Hide();
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

        // Attempt to find or create a lobby
        EmitSignal(SignalName.MatchMakingRequested, lobbyType);
    }
}
