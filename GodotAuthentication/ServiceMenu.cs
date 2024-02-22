using Godot;
using System;

public partial class ServiceMenu : Control
{
    [Signal]
    public delegate void ServiceButtonPressedEventHandler(int buttonIndex);

    private Button _entityServiceButton;
    private Button _gloablStatisticsServiceButton;
    private Button _identityServiceButton;
    private Button _playerStatisticsServiceButton;
    private Button _scriptServiceButton;
    private Button _xpServiceButton;
    private Button _virtualCurrencyServiceButton;

    public override void _Ready()
    {
        _xpServiceButton = GetNode<Button>("VBoxContainer/XPServiceButton");
        _entityServiceButton = GetNode<Button>("VBoxContainer/EntityServiceButton");
        _gloablStatisticsServiceButton = GetNode<Button>("VBoxContainer/GlobalStatisticsServiceButton");
        _identityServiceButton = GetNode<Button>("VBoxContainer/IdentityServiceButton");
        _playerStatisticsServiceButton = GetNode<Button>("VBoxContainer/PlayerStatisticsServiceButton");
        _scriptServiceButton = GetNode<Button>("VBoxContainer/ScriptServiceButton");
        _virtualCurrencyServiceButton = GetNode<Button>("VBoxContainer/VirtualCurrencyServiceButton");
    }

    /// <summary>
    /// Emit a signal supplying which button was pressed.
    /// Signal argument is defined in the editor.
    /// </summary>
    /// <param name="buttonIndex">Value of the pressed button. Defined in the editor.</param>
    private void OnButtonPressed(int buttonIndex)
    {
        EmitSignal(SignalName.ServiceButtonPressed, buttonIndex);
    }
}
