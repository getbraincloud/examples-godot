using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Post-match results + rematch-queue screen (BCLOUD-14489). Shows how everyone placed this
/// round and what it did to their leaderboard ranks, then either auto-rematches after
/// rematchMs or as soon as everyone queues up (see Main.TickRematchGate).
/// </summary>
public partial class MatchSummaryScreen : Control
{
	[Signal]
	public delegate void RematchReadyChangedEventHandler(bool ready);
	[Signal]
	public delegate void MainMenuRequestedEventHandler();

	private Label _lobbyInfoLabel;
	private Label _winnerLabel;
	private VBoxContainer _cardsContainer;
	private Label _countdownLabel;
	private Button _rematchButton;
	private Button _mainMenuButton;

	private long _rematchMs;
	private bool _iAmReady;
	private double _clock;
	private int _readyCount;
	private int _totalCount;

	// How long a player card waits for its leaderboard delta (polled from the GlobalEntity
	// PostMatchResults.js writes — see Main.TickMatchResultsPoll) before giving up and
	// showing "Leaderboard unavailable" instead of "Updating leaderboards..." forever — the
	// cloud script call is best-effort, so this is the backstop that keeps this screen from
	// looking permanently stuck if it never shows up.
	private const long LeaderboardResultTimeoutMs = 8000;
	private bool _leaderboardTimeoutHandled;

	// Cached args from the last RefreshResults call, so the timeout backstop above can force
	// a re-render with the same data once it fires.
	private List<MatchResultEntry> _lastEntries;
	private Dictionary<string, object> _lastLobby;
	private string _lastLocalCxId;
	private bool _lastLocalIsReady;

	public override void _Ready()
	{
		_lobbyInfoLabel = GetNode<Label>("VBoxContainer/LobbyInfoLabel");
		_winnerLabel = GetNode<Label>("VBoxContainer/WinnerLabel");
		_cardsContainer = GetNode<VBoxContainer>("VBoxContainer/CardsScroll/Cards");
		_countdownLabel = GetNode<Label>("VBoxContainer/CountdownLabel");
		_rematchButton = GetNode<Button>("VBoxContainer/ButtonsRow/RematchButton");
		_mainMenuButton = GetNode<Button>("VBoxContainer/ButtonsRow/MainMenuButton");

		_rematchButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnRematchPressed));
		_mainMenuButton.Connect(Button.SignalName.Pressed, new Callable(this, MethodName.OnMainMenuPressed));

		RefreshRematchButton();
	}

	public override void _Process(double delta)
	{
		_clock += delta;

		long elapsedMs = (long)(_clock * 1000);
		long remainingMs = Math.Max(0, _rematchMs - elapsedMs);
		long remainingSec = (remainingMs + 999) / 1000;
		if (_countdownLabel != null)
			_countdownLabel.Text = $"Next Round: {remainingSec / 60}:{(remainingSec % 60):D2}";

		if (!_leaderboardTimeoutHandled && elapsedMs >= LeaderboardResultTimeoutMs)
		{
			_leaderboardTimeoutHandled = true;
			RefreshResults(_lastEntries, _lastLobby, _lastLocalCxId, _lastLocalIsReady);
		}

		// Per-player auto-queue: if this player hasn't clicked "Queue for Rematch" themselves
		// by rematchMs, queue them automatically — matches the "if players do nothing, auto
		// rematch" requirement without waiting on anyone else.
		if (!_iAmReady && elapsedMs >= _rematchMs)
		{
			_iAmReady = true;
			RefreshRematchButton();
			EmitSignal(SignalName.RematchReadyChanged, true);
		}
	}

	/// <summary>Must be called once, right after instancing, before this screen does anything.</summary>
	public void Init(long arrivalTimeMs, long rematchMs)
	{
		_rematchMs = rematchMs;
		_clock = 0;
		_iAmReady = false;
	}

	/// <summary>
	/// Rebuilds the winner banner + player cards from the current match result (called once
	/// when this screen opens, again once the cloud leaderboard results arrive, and again on
	/// every lobby event while this screen is up so the ready-count stays live).
	/// </summary>
	public void RefreshResults(List<MatchResultEntry> entries, Dictionary<string, object> lobby, string localCxId, bool localIsReady)
	{
		_lastEntries = entries;
		_lastLobby = lobby;
		_lastLocalCxId = localCxId;
		_lastLocalIsReady = localIsReady;

		var lobbyId = lobby != null && lobby.ContainsKey("id") ? lobby["id"]?.ToString() ?? "" : "";
		var members = lobby != null && lobby.ContainsKey("members") ? (Dictionary<string, object>[])lobby["members"] : new Dictionary<string, object>[0];

		// Use localIsReady for our own row instead of lobby.members' entry — that entry is
		// briefly stale right after arriving here (Main clears it optimistically on END_MATCH,
		// but the server echo takes a few seconds to catch up), which used to flash "1/1" for a
		// moment before dropping to the correct "0/1".
		_iAmReady = localIsReady;

		_totalCount = members.Length;
		_readyCount = 0;
		foreach (var m in members)
		{
			bool isReady = (string)m["cxId"] == localCxId
				? localIsReady
				: m.ContainsKey("isReady") && Convert.ToBoolean(m["isReady"]);
			if (isReady) _readyCount++;
		}

		_lobbyInfoLabel.Text = $"Lobby {lobbyId} - {members.Length} Players";

		if (entries != null && entries.Count > 0)
		{
			var winner = entries[0];
			string winnerName = FindMemberName(members, winner.CxId);
			_winnerLabel.Text = $"{winnerName} wins the round, covering {winner.CoveragePct:F0}% of the board.";
			_winnerLabel.Show();
		}
		else
		{
			_winnerLabel.Text = "Waiting for results...";
			_winnerLabel.Show();
		}

		foreach (Node child in _cardsContainer.GetChildren()) child.QueueFree();
		if (entries != null)
		{
			foreach (var entry in entries)
				_cardsContainer.AddChild(BuildPlayerCard(entry, members, localCxId));
		}

		RefreshRematchButton();
	}

	private static string FindMemberName(Dictionary<string, object>[] members, string cxId)
	{
		foreach (var m in members)
			if ((string)m["cxId"] == cxId) return (string)m["name"];
		return "?";
	}

	private PanelContainer BuildPlayerCard(MatchResultEntry entry, Dictionary<string, object>[] members, string localCxId)
	{
		string name = "?";
		int colourIndex = 0;
		foreach (var m in members)
		{
			if ((string)m["cxId"] == entry.CxId)
			{
				name = (string)m["name"];
				var extra = (Dictionary<string, object>)m["extra"];
				colourIndex = extra.ContainsKey("colorIndex") ? Convert.ToInt32(extra["colorIndex"]) : 0;
				break;
			}
		}
		bool isMe = entry.CxId == localCxId;

		var card = new PanelContainer();
		var vbox = new VBoxContainer();
		card.AddChild(vbox);

		var header = new HBoxContainer();
		var rank = new Label { Text = "#" + entry.Rank };
		rank.AddThemeColorOverride("font_color", LeaderboardPanel.RankColor(entry.Rank));
		var dot = new ColorRect { CustomMinimumSize = new Vector2(10, 10), Color = Main.Colours[colourIndex % Main.Colours.Length] };
		var name_ = new Label { Text = name + (isMe ? " (YOU)" : "") };
		name_.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		if (isMe) name_.AddThemeColorOverride("font_color", new Color(0.35f, 1f, 0.45f));
		var coveragePct = new Label { Text = entry.CoveragePct.ToString("F0") + "%   COVERAGE" };
		if (isMe) coveragePct.AddThemeColorOverride("font_color", new Color(0.35f, 1f, 0.45f));

		header.AddChild(rank);
		header.AddChild(dot);
		header.AddChild(name_);
		header.AddChild(coveragePct);
		vbox.AddChild(header);

		vbox.AddChild(new Label { Text = $"+{entry.Beaten + 1} pts  ({entry.Beaten} beaten + 1 for playing)" });

		if (!entry.LbDelta.Ready)
		{
			vbox.AddChild(new Label { Text = _leaderboardTimeoutHandled ? "Leaderboard unavailable" : "Updating leaderboards..." });
		}
		else
		{
			bool anyPointsUp = entry.LbDelta.PointsLifetime.Improved || entry.LbDelta.PointsQuarterly.Improved;
			bool anyCoverageBest = entry.LbDelta.CoverageLifetime.Improved || entry.LbDelta.CoverageQuarterly.Improved;

			if (anyPointsUp)
				vbox.AddChild(new Label { Text = "^ Rank up - Opponents Beaten   " + PeriodsText(entry.LbDelta.PointsLifetime, entry.LbDelta.PointsQuarterly) });
			if (anyCoverageBest)
				vbox.AddChild(new Label { Text = "* Personal best - Coverage %   " + PeriodsText(entry.LbDelta.CoverageLifetime, entry.LbDelta.CoverageQuarterly) });
			if (!anyPointsUp && !anyCoverageBest)
				vbox.AddChild(new Label { Text = "No leaderboard rank change this round" });
		}

		return card;
	}

	// Builds the "Lifetime #before->#after   Quarterly #before->#after" tail for whichever
	// periods actually improved — periods that didn't improve are simply omitted.
	private static string PeriodsText(LeaderboardPeriodDelta lifetime, LeaderboardPeriodDelta quarterly)
	{
		var parts = new List<string>();
		if (lifetime.Improved) parts.Add($"Lifetime #{(lifetime.RankBefore < 0 ? "-" : lifetime.RankBefore.ToString())} -> #{lifetime.RankAfter}");
		if (quarterly.Improved) parts.Add($"Quarterly #{(quarterly.RankBefore < 0 ? "-" : quarterly.RankBefore.ToString())} -> #{quarterly.RankAfter}");
		return string.Join("   |   ", parts);
	}

	private void RefreshRematchButton()
	{
		if (_rematchButton == null) return;
		_rematchButton.Text = (_iAmReady ? "Queued for Rematch" : "Queue for Rematch") + $"  {_readyCount}/{_totalCount}";
		_rematchButton.Modulate = _iAmReady ? new Color(0.4f, 0.9f, 0.5f) : new Color(1, 1, 1);
	}

	private void OnRematchPressed()
	{
		_iAmReady = !_iAmReady;
		RefreshRematchButton();
		EmitSignal(SignalName.RematchReadyChanged, _iAmReady);
	}

	private void OnMainMenuPressed()
	{
		EmitSignal(SignalName.MainMenuRequested);
	}
}
