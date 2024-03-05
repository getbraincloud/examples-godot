using Godot;
using System;

public partial class Authentication : Control
{
    [Signal]
    public delegate void AuthenticationRequestedEventHandler();

    private BCManager _bcManager;

    private LineEdit _universalIDLine;
    private LineEdit _passwordLine;
    private Button _authButton;

    public override void _Ready()
    {
        _bcManager = GetNode<BCManager>("/root/BCManager");

        _universalIDLine = GetNode<LineEdit>("AuthFields/UniversalIDLine");
        _passwordLine = GetNode<LineEdit>("AuthFields/PasswordLine");
        _authButton = GetNode<Button>("AuthFields/AuthButton");

        _authButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnAuthenticateButtonPressed));
    }

    private void OnAuthenticateButtonPressed()
    {
        // Verify fields
        string universalID = _universalIDLine.Text;
        string password = _passwordLine.Text;

        if(string.IsNullOrEmpty(universalID) || string.IsNullOrEmpty(password))
        {
            GD.Print("One or more authentication fields were empty");
        }
        else
        {
            // Attempt authentication
            EmitSignal(SignalName.AuthenticationRequested);
            _bcManager.AuthenticateUniversal(universalID, password);
        }
        
    }
}
