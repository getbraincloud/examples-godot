using BrainCloud.UnityWebSocketsForWebGL.WebSocketSharp;
using Godot;
using System;

public partial class AuthenticationMenu : Control
{
    [Signal]
    public delegate void AuthenticatedEventHandler();

    private OptionButton _authenticationOptions;
    private VBoxContainer _idFields;
    private LineEdit _userIDField;
    private LineEdit _passwordField;
    private Label _authenticationMenuMessage;
    private Button _authenticateButton;

    private BCManager _brainCloud;

    public override void _Ready()
    {
        _authenticationOptions = GetNode<OptionButton>("AuthenticationFields/AuthenticationOptions");
        _idFields = GetNode<VBoxContainer>("AuthenticationFields/IDFields");
        _userIDField = GetNode<LineEdit>("AuthenticationFields/IDFields/UserIDLine");
        _passwordField = GetNode<LineEdit>("AuthenticationFields/IDFields/PasswordLine");
        _authenticationMenuMessage = GetNode<Label>("AuthenticationFields/AuthenticationMenuMessage");
        _authenticateButton = GetNode<Button>("AuthenticationFields/AuthenticateButton");

        _brainCloud = GetNode<BCManager>("/root/BCManager");
        _brainCloud.Connect(BCManager.SignalName.AuthenticationSuccess, new Callable(this, MethodName.OnAuthenticationSuccess));
        _brainCloud.Connect(BCManager.SignalName.AuthenticationFailure, new Callable(this, MethodName.OnAuthenticationFail));

        // Anonymous authentication is default, so hide id fields first
        _idFields.Hide();

        // Error message will be shown when needed
        _authenticationMenuMessage.Hide();

        // Populate AuthenticationOptions dropdown options
        _authenticationOptions.AddItem("Anonymous");
        _authenticationOptions.AddItem("Universal");
        _authenticationOptions.AddItem("Email");

        _authenticationOptions.Connect(OptionButton.SignalName.ItemSelected, new Callable(this, MethodName.OnAuthenticationOptionsItemSelected));
        _authenticateButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnAuthenticationRequested));
    }
    
    public void DisplayAuthenticationMessage(string errorMsg)
    {
        _authenticationMenuMessage.Text = errorMsg;
        _authenticationMenuMessage.Show();
        _authenticateButton.Disabled = false;
    }

    private void OnAuthenticationOptionsItemSelected(int index)
    {
        _userIDField.Clear();
        _passwordField.Clear();

        _authenticationMenuMessage.Hide();

        switch (index)
        {
            case 0:
                _idFields.Hide();
                break;
            case 1:
                _idFields.Show();
                _userIDField.PlaceholderText = "User ID";
                break;
            case 2:
                _idFields.Show();
                _userIDField.PlaceholderText = "Email";
                break;
            default:
                GD.Print("Invalid authentication type selected");
                break;
        }
    }

    private void OnAuthenticationRequested()
    {
        int authenticationType = _authenticationOptions.GetSelectedId();

        string userID = _userIDField.Text;
        string password = _passwordField.Text;

        _authenticationMenuMessage.Hide();
        _authenticateButton.Disabled = true;

        switch (authenticationType)
        {
            case 0:
                _brainCloud.RequestAnonymousAuthentication();
                break;
            case 1:
                if(!(userID.IsNullOrEmpty() || password.IsNullOrEmpty()))
                {
                    _brainCloud.RequestUniversalAuthentication(userID, password);
                    break;
                }

                DisplayAuthenticationMessage("Please fill in empty fields");
                break;
            case 2:
                if (!(userID.IsNullOrEmpty() || password.IsNullOrEmpty()))
                {
                    _brainCloud.RequestEmailPasswordAuthentication(userID, password);
                    break;
                }

                DisplayAuthenticationMessage("Please fill in empty fields");
                break;
            default:
                GD.Print("Invalid authentication type requested");
                break;
        }
    }

    private void OnAuthenticationSuccess()
    {
        EmitSignal(SignalName.Authenticated);
    }

    private void OnAuthenticationFail()
    {
        GD.Print("OnAuthenticationFail()");
    }
}
