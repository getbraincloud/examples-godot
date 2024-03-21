using Godot;
using System;

public partial class ErrorScreen : Control
{
    [Signal]
    public delegate void ErrorMessageDismissedEventHandler();
    
    private Label _errorMsgLabel;
    private Button _dismissButton;

    public override void _Ready()
    {
        _errorMsgLabel = GetNode<Label>("VBoxContainer/ErrorMsgLabel");
        _dismissButton = GetNode<Button>("VBoxContainer/DismissButton");

        // Connect Button listener(s)
        _dismissButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnDismissButtonPressed));
    }

    /// <summary>
    /// Display a brief description of the error.
    /// </summary>
    /// <param name="errorMsg">string containing brief description of the error.</param>
    public void SetErrorMessage(string errorMsg)
    {
        _errorMsgLabel.Text = errorMsg;
    }

    /// <summary>
    /// Send signal indicating that the user has read and dismissed the error message.
    /// </summary>
    private void OnDismissButtonPressed()
    {
        EmitSignal(SignalName.ErrorMessageDismissed);
    }
}
