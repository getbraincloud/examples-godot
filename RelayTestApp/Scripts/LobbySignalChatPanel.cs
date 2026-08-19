using Godot;
using System;

/// <summary>
/// This-lobby chat — via the Lobby service's SendSignal (not the Chat service — rides the RTT
/// connection the lobby already has, no separate channel needed). Unlike GlobalChatPanel, this
/// panel has no brainCloud calls of its own: Main owns the message history (so it survives this
/// screen being recreated between rounds) and pushes rows in via AddMessage/Clear; sending just
/// re-emits a signal for Main to relay.
/// </summary>
public partial class LobbySignalChatPanel : VBoxContainer
{
	[Signal]
	public delegate void SendRequestedEventHandler(string text);

	private ScrollContainer _scroll;
	private VBoxContainer _messagesContainer;
	private LineEdit _input;
	private Button _sendButton;

	public override void _Ready()
	{
		_scroll = GetNode<ScrollContainer>("Scroll");
		_messagesContainer = GetNode<VBoxContainer>("Scroll/Messages");
		_input = GetNode<LineEdit>("InputRow/Input");
		_sendButton = GetNode<Button>("InputRow/SendButton");

		_sendButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnSendPressed));
		_input.Connect(LineEdit.SignalName.TextSubmitted, new Callable(this, MethodName.OnInputSubmitted));
	}

	public void Clear()
	{
		foreach (Node child in _messagesContainer.GetChildren()) child.QueueFree();
	}

	public void AddMessage(string fromName, string text, bool isMe)
	{
		var row = new HBoxContainer();

		var fromLabel = new Label { Text = fromName + ":" };
		fromLabel.AddThemeColorOverride("font_color", isMe ? new Color(0.35f, 1f, 0.45f) : new Color(0.6f, 0.75f, 1f));

		var textLabel = new Label { Text = text };
		textLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		textLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		row.AddChild(fromLabel);
		row.AddChild(textLabel);
		_messagesContainer.AddChild(row);

		CallDeferred(MethodName.ScrollToBottom);
	}

	private void ScrollToBottom()
	{
		_scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
	}

	private void OnSendPressed() => Send();
	private void OnInputSubmitted(string text) => Send();

	private void Send()
	{
		string text = _input.Text?.Trim();
		if (string.IsNullOrEmpty(text)) return;
		_input.Text = "";
		EmitSignal(SignalName.SendRequested, text);
	}
}
