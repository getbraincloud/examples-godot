using BrainCloud.UnityWebSocketsForWebGL.WebSocketSharp;
using Godot;
using GodotPlugins.Game;
using System;

public partial class Identity : Control
{
    [Signal]
    public delegate void AttachEmailIdentityRequestedEventHandler();

    private LineEdit _emailField;
    private LineEdit _emailPasswordField;
    private LineEdit _universalIDField;
    private LineEdit _universalPasswordField;

    private Button _attachEmailButton;
    private Button _mergeEmailButton;
    private Button _attachUniversalIDButton;
    private Button _mergeUniversalIDButton;

    private Button _currentActionButton;

    private BCManager _brainCloud;

    public override void _Ready()
    {        
        _emailField = GetNode<LineEdit>("VBoxContainer/EmailIdentitySection/EmailLine");
        _emailPasswordField = GetNode<LineEdit>("VBoxContainer/EmailIdentitySection/EmailPasswordLine");
        _universalIDField = GetNode<LineEdit>("VBoxContainer/UniversalIdentitySection/UserIDLine");
        _universalPasswordField = GetNode<LineEdit>("VBoxContainer/UniversalIdentitySection/UniversalPasswordLine");

        _attachEmailButton = GetNode<Button>("VBoxContainer/EmailIdentitySection/EmailAttachButton");
        _mergeEmailButton = GetNode<Button>("VBoxContainer/EmailIdentitySection/EmailMergeButton");
        _attachUniversalIDButton = GetNode<Button>("VBoxContainer/UniversalIdentitySection/UniversalAttachButton");
        _mergeUniversalIDButton = GetNode<Button>("VBoxContainer/UniversalIdentitySection/UniversalMergeButton");

        _brainCloud = GetNode<BCManager>("/root/BCManager");

        _attachEmailButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnAttachEmailButtonPressed));
        _mergeEmailButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnMergeEmailButtonPressed));
        _attachUniversalIDButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnAttachUniversalIDButtonPressed));
        _mergeUniversalIDButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnMergeUniversalIDButtonPressed));

        _brainCloud.Connect(BCManager.SignalName.IdentityAttachSuccess, new Callable(this, MethodName.OnIdentityAttachSuccess));
        _brainCloud.Connect(BCManager.SignalName.IdentityAttachFailure, new Callable(this, MethodName.OnIdentityAttachFailure));
        _brainCloud.Connect(BCManager.SignalName.IdentityMergeSuccess, new Callable(this, MethodName.OnIdentityMergeSuccess));
        _brainCloud.Connect(BCManager.SignalName.IdentityMergeFailure, new Callable(this, MethodName.OnIdentityMergeFailure));

        ClearFields();
    }

    private bool FieldsAreEmpty(string userID, string password)
    {
        GD.Print("Checking for empty fields . . .");

        if(userID.IsNullOrEmpty() || password.IsNullOrEmpty())
        {
            GD.Print("One or more fields were empty");

            return true;
        }

        return false;
    }

    private void ClearFields()
    {
        _emailField.Clear();
        _emailPasswordField.Clear();
        _universalIDField.Clear();
        _universalPasswordField.Clear();
    }

    private void OnAttachEmailButtonPressed()
    {
        string email = _emailField.Text;
        string password = _emailPasswordField.Text;

        if(!FieldsAreEmpty(email, password))
        {
            _brainCloud.RequestEmailIdentityAttach(email, password);
        }
    }

    private void OnMergeEmailButtonPressed()
    {
        string email = _emailField.Text;
        string password = _emailPasswordField.Text;

        if (!FieldsAreEmpty(email, password))
        {
            _brainCloud.RequestEmailIdentityMerge(email, password);
        }
    }

    private void OnAttachUniversalIDButtonPressed()
    {
        string universalID = _universalIDField.Text;
        string password = _universalPasswordField.Text;

        if (!FieldsAreEmpty(universalID, password))
        {
            _brainCloud.RequestUniversalIdentityAttach(universalID, password);
        }
    }

    private void OnMergeUniversalIDButtonPressed()
    {
        string universalID = _universalIDField.Text;
        string password = _universalPasswordField.Text;

        if (!FieldsAreEmpty(universalID, password))
        {
            _brainCloud.RequestUniversalIdentityMerge(universalID, password);
        }
    }

    private void OnIdentityAttachSuccess()
    {
        GD.Print("Identity attached!");
        ClearFields();
    }

    private void OnIdentityAttachFailure()
    {
        GD.Print("Identity failed to attach");
    }

    private void OnIdentityMergeSuccess()
    {
        GD.Print("Identity merged!");
        ClearFields();
    }

    private void OnIdentityMergeFailure()
    {
        GD.Print("Identity failed to merge");
    }
}
