using BrainCloud;
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Shared app-wide "Global" chat channel — used from both the lobby-select (main menu) and
/// lobby screens, so channel resolution / history / send all live here. brainCloud's chat
/// calls all require RTT to be enabled (RTT_NOT_ENABLED otherwise); Main enables RTT as soon
/// as the lobby-select screen is reached, which covers both mount points. Live RTT push:
/// ChannelConnect both registers the listener AND returns initial history in one call; every
/// message after that (including our own sends, edits, and deletes) arrives via
/// RegisterRTTChatCallback — see knowledge-articles/01-chat.md. No re-fetch after posting.
/// </summary>
public partial class GlobalChatPanel : VBoxContainer
{
	private const string ChannelType = "gl";
	private const string ChannelSubId = "gl";
	private const int MaxHistory = 30;
	private const double RetryBackoffSec = 5.0;

	private BrainCloudWrapper _bc;
	private string _channelId;
	private bool _resolving;
	private double _retryAtSec = 0;
	private double _clock = 0;

	// msgId -> row, so an UPDATE/DELETE event (keyed by msgId) can find its row again. A
	// message updated or deleted after it's scrolled out isn't in here and the event is a
	// no-op, which is correct (nothing on screen to change).
	private readonly Dictionary<string, HBoxContainer> _rowsByMsgId = new();

	private ScrollContainer _scroll;
	private VBoxContainer _messagesContainer;
	private Label _statusLabel;
	private LineEdit _input;
	private Button _sendButton;

	public override void _Ready()
	{
		_scroll = GetNode<ScrollContainer>("Scroll");
		_messagesContainer = GetNode<VBoxContainer>("Scroll/Messages");
		_statusLabel = GetNode<Label>("StatusLabel");
		_input = GetNode<LineEdit>("InputRow/Input");
		_sendButton = GetNode<Button>("InputRow/SendButton");

		_sendButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnSendPressed));
		_input.Connect(LineEdit.SignalName.TextSubmitted, new Callable(this, MethodName.OnInputSubmitted));

		_statusLabel.Text = "Connecting...";
	}

	public override void _Process(double delta)
	{
		_clock += delta;
		EnsureChannel();
	}

	public override void _ExitTree()
	{
		if (!string.IsNullOrEmpty(_channelId))
			_bc?.RTTService.DeregisterRTTChatCallback();
	}

	/// <summary>Must be called once, right after instancing, before this panel does anything.</summary>
	public void Init(BrainCloudWrapper bc)
	{
		_bc = bc;
	}

	// Resolves the shared global channel once RTT is up, then connects (which also returns
	// initial history in the same response) and registers for live push. Safe to call every
	// frame — no-ops once resolved, in flight, or backing off after a recent failure (so a
	// persistent failure can't turn into a same-call-every-frame loop).
	private void EnsureChannel()
	{
		if (_bc == null || _channelId != null || _resolving) return;
		if (!_bc.RTTService.IsRTTEnabled()) return;
		if (_clock < _retryAtSec) return;

		_resolving = true;
		_bc.ChatService.GetChannelId(ChannelType, ChannelSubId,
			(jsonResponse, cbObject) =>
			{
				var response = BrainCloud.JsonFx.Json.JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
				var data = response.ContainsKey("data") ? response["data"] as Dictionary<string, object> : null;
				string channelId = data != null && data.ContainsKey("channelId") ? data["channelId"] as string : null;
				if (string.IsNullOrEmpty(channelId))
				{
					_resolving = false;
					return;
				}
				ConnectChannel(channelId);
			},
			(status, reasonCode, jsonError, cbObject) =>
			{
				_resolving = false;
				_retryAtSec = _clock + RetryBackoffSec;
			});
	}

	private void ConnectChannel(string channelId)
	{
		_bc.ChatService.ChannelConnect(channelId, MaxHistory,
			(jsonResponse, cbObject) =>
			{
				_resolving = false;
				_channelId = channelId;
				_statusLabel.Hide();

				var response = BrainCloud.JsonFx.Json.JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
				var data = response.ContainsKey("data") ? response["data"] as Dictionary<string, object> : null;
				var messages = data != null && data.ContainsKey("messages") ? data["messages"] as object[] : null;

				if (messages != null)
				{
					// Server already returns these ascending/oldest-first (it re-sorts from its
					// native descending storage order before responding) — no reverse needed.
					// Same order as the RTT push appends, so history and live messages never conflict.
					for (int i = 0; i < messages.Length; i++)
					{
						if (messages[i] is not Dictionary<string, object> m) continue;
						AddMessageRowFromWire(m);
					}
				}
				ScrollToBottom();

				_bc.RTTService.RegisterRTTChatCallback(OnRttChatEvent);
			},
			(status, reasonCode, jsonError, cbObject) =>
			{
				_resolving = false;
				_retryAtSec = _clock + RetryBackoffSec;
			});
	}

	// New messages, edits, and deletes all arrive on the same RTT event, keyed by msgId.
	private void OnRttChatEvent(string jsonResponse)
	{
		var msg = BrainCloud.JsonFx.Json.JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
		string operation = msg.ContainsKey("operation") ? msg["operation"] as string : null;
		var data = msg.ContainsKey("data") ? msg["data"] as Dictionary<string, object> : null;
		if (data == null) return;

		if (operation == "INCOMING")
		{
			AddMessageRowFromWire(data);
			ScrollToBottom();
		}
		else if (operation == "UPDATE")
		{
			// Same shape as INCOMING (full message, edited content) — update the existing
			// row's text in place so it doesn't jump to the bottom of the scroll.
			string msgId = data.ContainsKey("msgId") ? data["msgId"] as string : null;
			if (msgId != null && _rowsByMsgId.TryGetValue(msgId, out var row))
			{
				var content = data.ContainsKey("content") ? data["content"] as Dictionary<string, object> : null;
				string text = content != null && content.ContainsKey("text") ? content["text"] as string : null;
				(row.GetChild(1) as Label).Text = text ?? "";
			}
		}
		else if (operation == "DELETE")
		{
			// DELETE's payload is just {chId, msgId} -- no content/from -- so only msgId is usable here.
			string msgId = data.ContainsKey("msgId") ? data["msgId"] as string : null;
			if (msgId != null && _rowsByMsgId.TryGetValue(msgId, out var row))
			{
				row.QueueFree();
				_rowsByMsgId.Remove(msgId);
			}
		}
	}

	private void AddMessageRowFromWire(Dictionary<string, object> m)
	{
		var from = m.ContainsKey("from") ? m["from"] as Dictionary<string, object> : null;
		var content = m.ContainsKey("content") ? m["content"] as Dictionary<string, object> : null;
		string msgId = m.ContainsKey("msgId") ? m["msgId"] as string : null;
		string fromName = from != null && from.ContainsKey("name") ? from["name"] as string : null;
		string text = content != null && content.ContainsKey("text") ? content["text"] as string : null;
		AddMessageRow(msgId, string.IsNullOrEmpty(fromName) ? "Player" : fromName, text ?? "");
	}

	private void AddMessageRow(string msgId, string fromName, string text)
	{
		var row = new HBoxContainer();

		var fromLabel = new Label { Text = fromName + ":" };
		fromLabel.AddThemeColorOverride("font_color", new Color(0.6f, 0.75f, 1f));

		var textLabel = new Label { Text = text };
		textLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
		textLabel.SizeFlagsHorizontal = SizeFlags.ExpandFill;

		row.AddChild(fromLabel);
		row.AddChild(textLabel);
		_messagesContainer.AddChild(row);

		if (!string.IsNullOrEmpty(msgId))
			_rowsByMsgId[msgId] = row;
	}

	private void ScrollToBottom()
	{
		// Deferred so the scroll container has processed the new children's sizes first.
		CallDeferred(MethodName.DoScrollToBottom);
	}

	private void DoScrollToBottom()
	{
		_scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
	}

	private void OnSendPressed() => Send();
	private void OnInputSubmitted(string text) => Send();

	private void Send()
	{
		string text = _input.Text?.Trim();
		if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(_channelId)) return;
		_input.Text = "";
		// Delivered back to us via OnRttChatEvent, same as every other member — no re-fetch needed.
		_bc.ChatService.PostChatMessageSimple(_channelId, text, true,
			(jsonResponse, cbObject) => { },
			(status, reasonCode, jsonError, cbObject) => { });
	}
}
