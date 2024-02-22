using Godot;
using System;

public partial class Authentication : Control
{
    [Signal]
    public delegate void AuthenticationRequestedEventHandler();

    private BCManager m_BCManager;

    private LineEdit m_UniversalIDLine;
    private LineEdit m_PasswordLine;
    private Button m_AuthButton;

    public override void _Ready()
    {
        m_BCManager = GetNode<BCManager>("/root/BCManager");

        m_UniversalIDLine = GetNode<LineEdit>("AuthFields/UniversalIDLine");
        m_PasswordLine = GetNode<LineEdit>("AuthFields/PasswordLine");
        m_AuthButton = GetNode<Button>("AuthFields/AuthButton");

        m_AuthButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.AttemptAuthentication));
    }

    private void AttemptAuthentication()
    {
        // Verify fields
        string universalID = m_UniversalIDLine.Text;
        string password = m_PasswordLine.Text;

        if(string.IsNullOrEmpty(universalID) || string.IsNullOrEmpty(password))
        {
            GD.Print("One or more authentication fields were empty");
        }
        else
        {
            // Attempt authentication
            EmitSignal(SignalName.AuthenticationRequested);
            m_BCManager.AuthenticateUniversal(universalID, password);
        }
        
    }
}
