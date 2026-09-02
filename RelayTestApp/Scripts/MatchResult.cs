using System.Collections.Generic;

// One leaderboard period's (Lifetime or Quarterly) rank-before/after for a single board.
// "Improved" is the trigger for whether the summary screen shows a badge at all — for the
// points boards that means the rank got numerically better; for the coverage boards it means
// this round's score replaced a lower personal best.
public class LeaderboardPeriodDelta
{
	public bool Improved;
	public int RankBefore = -1; // -1 = unranked/no score yet before this round
	public int RankAfter = -1;
}

// Personal leaderboard-rank movement from posting this round's score, across all four boards.
// Computed server-side in one PostMatchResults cloud script call made by the host — see
// Main.HostPostMatchResultsToCloud (host, applied directly from the script response) and
// Main.TickMatchResultsPoll (everyone else, polled from the GlobalEntity that call writes).
public class LeaderboardDelta
{
	public bool Ready; // true once this entry's delta has been computed (host) or received (others)
	public LeaderboardPeriodDelta PointsLifetime = new();
	public LeaderboardPeriodDelta PointsQuarterly = new();
	public LeaderboardPeriodDelta CoverageLifetime = new(); // "improved" = new coverage personal best
	public LeaderboardPeriodDelta CoverageQuarterly = new();
}

// One player's entry in a host-broadcast, authoritative match_result.
public class MatchResultEntry
{
	public string CxId;
	public int Rank;
	public float CoveragePct;
	public int Beaten;
	public LeaderboardDelta LbDelta = new(); // filled in asynchronously — see MatchResultData
}

// Authoritative snapshot of a finished match's standings, broadcast by the (possibly
// migrated) host and applied identically by every client.
public class MatchResultData
{
	public bool Valid;
	public int Round;
	public List<MatchResultEntry> Entries = new();
}
