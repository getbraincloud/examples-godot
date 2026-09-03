using BrainCloud;
using Godot;
using System;
using System.Collections.Generic;

/// <summary>
/// Shared leaderboard viewer — top 5 + "you" row, toggleable between the two boards this app
/// posts to (points / coverage) and Lifetime vs Quarterly. Mirrors leaderboardPanel.cpp /
/// LeaderboardPanel.js.
///
/// NOTE: scores are only posted once a match's coverage/win-ranking is computed and submitted
/// — until some client posts to these boards, they'll show "No scores yet."
/// </summary>
public partial class LeaderboardPanel : VBoxContainer
{
	// Defaults match leaderboards configured on the app (Design > Leaderboards) — same default
	// ids the cpp/react ports read from and post to. Main overwrites these once it resolves the
	// PointsLeaderboardId/CoverageLeaderboardId (+Quarterly) global-property overrides, so this
	// viewer stays in sync with whatever board hostPostMatchResultsToCloud actually posted to
	// (mirrors cpp reading state.*LeaderboardId directly, and js sharing LEADERBOARD_IDS).
	public static string PointsLeaderboardId = "CursorParty_Points";
	public static string PointsLeaderboardIdQuarterly = "CursorParty_Points_Quarterly";
	public static string CoverageLeaderboardId = "CursorParty_HighestCoverage";
	public static string CoverageLeaderboardIdQuarterly = "CursorParty_HighestCoverage_Quarterly";

	private class Entry
	{
		public string Name;
		public long Score;
		public int Rank;
	}

	private BrainCloudWrapper _bc;
	private string _boardType = "points"; // "points" | "coverage"
	private string _period = "lifetime"; // "lifetime" | "quarterly"

	// key = "<boardType>:<period>"
	private readonly Dictionary<string, List<Entry>> _topByKey = new();
	private readonly Dictionary<string, Entry> _selfByKey = new();
	private readonly HashSet<string> _pendingKeys = new();

	private Button _pointsBtn, _coverageBtn, _lifetimeBtn, _quarterlyBtn;
	private VBoxContainer _rowsContainer;

	public override void _Ready()
	{
		_pointsBtn = GetNode<Button>("BoardRow/PointsButton");
		_coverageBtn = GetNode<Button>("BoardRow/CoverageButton");
		_lifetimeBtn = GetNode<Button>("PeriodRow/LifetimeButton");
		_quarterlyBtn = GetNode<Button>("PeriodRow/QuarterlyButton");
		_rowsContainer = GetNode<VBoxContainer>("Rows");

		_pointsBtn.Pressed += () => SetBoardType("points");
		_coverageBtn.Pressed += () => SetBoardType("coverage");
		_lifetimeBtn.Pressed += () => SetPeriod("lifetime");
		_quarterlyBtn.Pressed += () => SetPeriod("quarterly");

		RefreshToggleStyles();
	}

	/// <summary>Must be called once, right after instancing, before this panel does anything.</summary>
	public void Init(BrainCloudWrapper bc)
	{
		_bc = bc;
		FetchIfNeeded();
	}

	private string CurrentKey() => _boardType + ":" + _period;

	private string CurrentLeaderboardId()
	{
		if (_boardType == "coverage")
			return _period == "quarterly" ? CoverageLeaderboardIdQuarterly : CoverageLeaderboardId;
		return _period == "quarterly" ? PointsLeaderboardIdQuarterly : PointsLeaderboardId;
	}

	private void SetBoardType(string boardType)
	{
		if (_boardType == boardType) return;
		_boardType = boardType;
		RefreshToggleStyles();
		FetchIfNeeded();
		RenderRows();
	}

	private void SetPeriod(string period)
	{
		if (_period == period) return;
		_period = period;
		RefreshToggleStyles();
		FetchIfNeeded();
		RenderRows();
	}

	private void RefreshToggleStyles()
	{
		SetActive(_pointsBtn, _boardType == "points");
		SetActive(_coverageBtn, _boardType == "coverage");
		SetActive(_lifetimeBtn, _period == "lifetime");
		SetActive(_quarterlyBtn, _period == "quarterly");
	}

	private void SetActive(Button b, bool active)
	{
		b.Modulate = active ? new Color(0.4f, 0.7f, 1f) : new Color(0.6f, 0.6f, 0.6f);
	}

	// Fetches the top 5 + the local player's own rank for the currently-selected board/period
	// combo. Guarded so it only ever fires once per combo per session — switching tabs back
	// and forth re-shows cached results, not a re-fetch.
	private void FetchIfNeeded()
	{
		if (_bc == null) return;
		string key = CurrentKey();
		if (_topByKey.ContainsKey(key) || _pendingKeys.Contains(key)) { RenderRows(); return; }
		_pendingKeys.Add(key);

		string leaderboardId = CurrentLeaderboardId();

		_bc.SocialLeaderboardService.GetGlobalLeaderboardPage(leaderboardId, BrainCloudSocialLeaderboard.SortOrder.HIGH_TO_LOW, 0, 4,
			(jsonResponse, cbObject) =>
			{
				_topByKey[key] = ParseEntries(jsonResponse);
				_pendingKeys.Remove(key);
				if (key == CurrentKey()) RenderRows();
			},
			(status, reasonCode, jsonError, cbObject) =>
			{
				_topByKey[key] = new List<Entry>();
				_pendingKeys.Remove(key);
				if (key == CurrentKey()) RenderRows();
			});

		// beforeCount=0/afterCount=0 returns just the current player's own entry.
		_bc.SocialLeaderboardService.GetGlobalLeaderboardView(leaderboardId, BrainCloudSocialLeaderboard.SortOrder.HIGH_TO_LOW, 0, 0,
			(jsonResponse, cbObject) =>
			{
				var entries = ParseEntries(jsonResponse);
				if (entries.Count > 0) _selfByKey[key] = entries[0];
				if (key == CurrentKey()) RenderRows();
			},
			(status, reasonCode, jsonError, cbObject) => { });
	}

	private List<Entry> ParseEntries(string jsonResponse)
	{
		var result = new List<Entry>();
		var response = BrainCloud.JsonFx.Json.JsonReader.Deserialize<Dictionary<string, object>>(jsonResponse);
		var data = response.ContainsKey("data") ? response["data"] as Dictionary<string, object> : null;
		var arr = data != null && data.ContainsKey("leaderboard") ? data["leaderboard"] as object[] : null;
		if (arr == null) return result;

		foreach (var o in arr)
		{
			if (o is not Dictionary<string, object> e) continue;
			var entryData = e.ContainsKey("data") ? e["data"] as Dictionary<string, object> : null;
			string name = entryData != null && entryData.ContainsKey("name") ? entryData["name"] as string : null;
			result.Add(new Entry
			{
				Name = string.IsNullOrEmpty(name) ? "Player" : name,
				Score = e.ContainsKey("score") ? Convert.ToInt64(e["score"]) : 0,
				Rank = e.ContainsKey("rank") ? Convert.ToInt32(e["rank"]) : 0
			});
		}
		return result;
	}

	private void RenderRows()
	{
		foreach (Node child in _rowsContainer.GetChildren()) child.QueueFree();

		string key = CurrentKey();
		var top = _topByKey.ContainsKey(key) ? _topByKey[key] : new List<Entry>();
		_selfByKey.TryGetValue(key, out var self);

		if (top.Count == 0)
		{
			_rowsContainer.AddChild(new Label { Text = "No scores yet — be the first!" });
		}
		else
		{
			foreach (var e in top) _rowsContainer.AddChild(BuildRow(e, false));
		}

		// "You" row — only when it's not already visible in the top 5.
		if (self != null && !top.Exists(e => e.Rank == self.Rank))
		{
			if (top.Count > 0) _rowsContainer.AddChild(new Label { Text = "..." });
			_rowsContainer.AddChild(BuildRow(self, true));
		}
	}

	private HBoxContainer BuildRow(Entry e, bool isMe)
	{
		var row = new HBoxContainer();

		var rank = new Label { Text = "#" + e.Rank, CustomMinimumSize = new Vector2(40, 0) };
		rank.AddThemeColorOverride("font_color", RankColor(e.Rank));

		var name = new Label { Text = e.Name + (isMe ? " (You)" : "") };
		name.SizeFlagsHorizontal = SizeFlags.ExpandFill;
		if (isMe) name.AddThemeColorOverride("font_color", new Color(0.35f, 1f, 0.45f));

		var score = new Label { Text = FormatScore(e.Score) };
		if (isMe) score.AddThemeColorOverride("font_color", new Color(0.35f, 1f, 0.45f));

		row.AddChild(rank);
		row.AddChild(name);
		row.AddChild(score);
		return row;
	}

	// The coverage board's raw score is basis points (coveragePct*100, since brainCloud
	// leaderboard scores are integers) — divide back down for display. The points board's raw
	// score is already the real value.
	private string FormatScore(long score)
	{
		if (_boardType == "coverage") return (score / 100.0).ToString("F1") + "%";
		return score.ToString("N0");
	}

	// Gold/silver/bronze/white — shared with the lobby member list's rank display.
	public static Color RankColor(int rank)
	{
		if (rank == 1) return new Color("#ffd700");
		if (rank == 2) return new Color("#c0c0c0");
		if (rank == 3) return new Color("#cc8833");
		return new Color(1, 1, 1);
	}
}
