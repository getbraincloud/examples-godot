using Godot;
using System;

public partial class LoadingScreen : Control
{
    [Signal]
    public delegate void CancelRequestedEventHandler();

    // Message indicating the current process / next screen
    private Label _loadingMessage;

    // Elapsed-time readout while SearchStartMs is set (matchmaking search / lobby join) —
    // mirrors the cpp/JS loading screens, which both show a live "M:SS" timer here.
    private Label _timerLabel;

    // Cancel the current process and return to the previous screen
    private Button _cancelButton;

    // 0 = no timer shown. Set via EnableTimerAndCancel; survives across LoadScene's
    // repeated re-instantiation of this scene for each matchmaking sub-step (Enabling RTT ->
    // Pinging regions -> Finding/Creating lobby -> Joining lobby) because Main passes the
    // same start time through to every new instance.
    private long _searchStartMs = 0;

    public override void _Ready()
    {
        _loadingMessage = GetNode<Label>("VBoxContainer/LoadingMessage");
        _timerLabel = GetNode<Label>("VBoxContainer/TimerLabel");
        _cancelButton = GetNode<Button>("VBoxContainer/CancelButton");

        _cancelButton.Connect(Button.SignalName.Pressed, Callable.From(() => EmitSignal(SignalName.CancelRequested)));
    }

    public override void _Process(double delta)
    {
        if (_searchStartMs == 0) return;
        long elapsedSec = (DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - _searchStartMs) / 1000;
        _timerLabel.Text = $"{elapsedSec / 60}:{elapsedSec % 60:D2}";
    }

    /// <summary>
    /// Display a message indicating the current process / next screen.
    /// </summary>
    /// <param name="loadingMessage"></param>
    public void SetLoadingMessage(string loadingMessage)
    {
        _loadingMessage.Text = loadingMessage;
    }

    /// <summary>
    /// Shows the elapsed-time readout (ticking from startTimeMs, so it keeps counting up
    /// correctly across every LoadScene re-instantiation during one matchmaking attempt) and
    /// a working Cancel button. Only called for the cancellable matchmaking-search steps —
    /// mirrors cpp's canCancel gate (JoiningLobby/Starting) and JS's 'joiningLobby' screen.
    /// </summary>
    public void EnableTimerAndCancel(long startTimeMs)
    {
        _searchStartMs = startTimeMs;
        _timerLabel.Show();
        _cancelButton.Show();
    }
}
