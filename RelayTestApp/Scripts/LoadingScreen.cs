using Godot;
using System;

public partial class LoadingScreen : Control
{
    // Message indicating the current process / next screen
    private Label _loadingMessage;

    // Cancel the current process and return to the previous screen
    private Button _cancelButton;

    public override void _Ready()
    {
        _loadingMessage = GetNode<Label>("VBoxContainer/LoadingMessage");
        _cancelButton = GetNode<Button>("VBoxContainer/CancelButton");

        // TODO:  implement cancel button
        _cancelButton.Hide();
    }

    /// <summary>
    /// Display a message indicating the current process / next screen.
    /// </summary>
    /// <param name="loadingMessage"></param>
    public void SetLoadingMessage(string loadingMessage)
    {
        _loadingMessage.Text = loadingMessage;
    }
}
