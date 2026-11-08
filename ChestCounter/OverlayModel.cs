using SquadChests;

namespace ChestCounter;

/// <summary>A short message next to the big number after a game was counted ("+67").</summary>
record Flash(string Text, Color Color, DateTime Until);

/// <summary>Turns the stats into what the overlay shows.</summary>
static class OverlayModel
{
    /// <summary>A break longer than this between games starts a new session.</summary>
    public static readonly TimeSpan SessionGap = TimeSpan.FromMinutes(45);

    public record Session(DateTime? Start, List<Stats.GameStats> Games, bool Active);

    /// <summary>
    /// Current session: games played back to back (less than <see cref="SessionGap"/> between them), ending with
    /// the game in progress or one that ended recently. "New session" in the menu starts a fresh one.
    /// </summary>
    public static Session CurrentSession(Stats.Summary s, DateTime? inGameSince, DateTime? manualStart, DateTime now)
    {
        var games = new List<Stats.GameStats>(); // newest first
        DateTime? prevStart = inGameSince;
        for (var i = s.All.Count - 1; i >= 0; i--)
        {
            var g = s.All[i];
            if (g.Match.Started is not DateTime start) continue;
            if (manualStart != null && start < manualStart) break;
            var end = start.AddSeconds(g.Match.LengthSeconds);
            var next = prevStart ?? now;   // start of the following game (or now for the newest)
            if (next - end > SessionGap) break;
            games.Add(g);
            prevStart = start;
        }

        var lastActivity = new[] { games.FirstOrDefault()?.Match is { } m ? m.Started?.AddSeconds(m.LengthSeconds) : null, inGameSince, manualStart }
            .Max();
        var active = inGameSince != null || (lastActivity != null && now - lastActivity < SessionGap);
        if (!active) return new Session(null, [], false);
        var first = games.LastOrDefault()?.Match.Started;
        var sessionStart = manualStart ?? first ?? inGameSince;
        return new Session(sessionStart, games, true);
    }

    public static OverlayForm.View Build(Stats.Summary s, string status, (int Done, int Total)? progress, DateTime? inGameSince,
        DateTime? manualStart, Flash? flash, string? hint, Color hintColor, DateTime now, int page = 0)
    {
        var graph = page == 1;
        var v = new OverlayForm.View();
        var session = CurrentSession(s, inGameSince, manualStart, now);
        var sGames = session.Games;
        var sChests = sGames.Sum(g => g.Chests);
        const string none = "-";

        // top row: chests last game (green for a while after it's counted), total, games, per game
        var flashing = flash != null && now < flash.Until;
        v.Headline.Add((s.LastGame?.Chests.ToString() ?? none, "LAST GAME", flashing ? flash!.Color : OverlayForm.Gold));

        v.Headline.Add((s.Chests.ToString("N0"), "TOTAL CHESTS", null));
        v.Headline2.Add((s.Games.ToString("N0"), "GAMES", null));
        v.Headline2.Add((s.Games > 0 ? s.PerGame.ToString("F1") : none, "AVERAGE", null));
        v.Headline2.Add((s.BestGame?.Chests.ToString() ?? none, "BEST GAME", null));
        if (s.HasTreasureCounts)
            v.Headline2.Add((s.Treasures.ToString("N0"), "TREASURES", flashing && s.LastGame?.Match.OwnTreasures > 0 ? flash!.Color : OverlayForm.Gold));

        // by mode: chests | games | per game | per min
        static string PerMin(IEnumerable<Stats.GameStats> games)
        {
            var list = games.ToList();
            var min = list.Sum(g => g.Match.LengthSeconds) / 60;
            return min > 0 ? (list.Sum(g => g.Chests) / min).ToString("F2") : none;
        }
        string[] Cells(IEnumerable<Stats.GameStats> games)
        {
            var list = games.ToList();
            if (list.Count == 0) return [none, "0", none, none];
            var chests = list.Sum(g => g.Chests);
            return [chests.ToString("N0"), list.Count.ToString("N0"), list.Max(g => g.Chests).ToString(), ((double)chests / list.Count).ToString("F1")];
        }

        if (s.Modes.Count > 0)
            v.Tables.Add(new(["mode", "chests", "games", "best", "p/g"],
                s.Modes.Select(m => new OverlayForm.TableRow(Short(m.Mode), Cells(s.All.Where(g => g.Mode == m.Mode)), Bold: false)).ToList()));

        if (s.Treasure.Count > 0)
            v.Tables.Add(new(null, [new("treasure", [$"{s.Treasure[^1].Items}, {s.Treasure[^1].Match.Started:MMM d}"], OverlayForm.Gold)]));

        if (graph)
        {
            // chests per game, oldest first; the trend line is the average of the last 10 games at each point
            const int window = 10, shown = 100;
            var all = s.All.Select(g => g.Chests).ToList();
            var trend = all.Select((_, i) => all.Skip(Math.Max(0, i - window + 1)).Take(Math.Min(window, i + 1)).Average()).ToList();
            var skip = Math.Max(0, all.Count - shown);
            v.Graph = all.Skip(skip).ToList();
            v.GraphTrend = trend.Skip(skip).ToList();
            v.GraphWindow = window;
            v.GraphAvg = s.PerGame;
        }

        // status only when something is going on
        var (text, color) = StatusText(status, progress, inGameSince, now);
        // in a game / reading the game that just ended: only a small dot in the corner, no extra line
        if (progress == null && (status == GameWatcher.InGameStatus || status.StartsWith("Reading your last game")))
            v.Dot = status == GameWatcher.InGameStatus ? OverlayForm.Gold : OverlayForm.Green;
        else if (text != "watching")
        {
            v.Status = text;
            v.StatusColor = color;
        }
        v.Hint = hint;
        v.HintColor = hintColor;
        return v;
    }

    static (string, Color) StatusText(string status, (int Done, int Total)? progress, DateTime? inGameSince, DateTime now)
    {
        if (progress is var (d, t)) return ($"reading {d}/{t}", OverlayForm.Gold);
        if (status == GameWatcher.InGameStatus)
            return (inGameSince is DateTime since ? $"in game {Duration(now - since)}  ·  counts when it ends" : "in game  ·  counts when it ends", OverlayForm.Gold);
        if (status.StartsWith("Reading your last game")) return ("reading last game", OverlayForm.Gold);
        if (status.StartsWith("Reading")) return ("reading...", OverlayForm.Gold);
        if (status == GameWatcher.WatchingStatus) return ("watching", OverlayForm.Dim);
        if (status.Contains("folder not found")) return ("replay folder not found", OverlayForm.Red);
        if (status.StartsWith("Starting")) return ("starting...", OverlayForm.Dim);
        return ("error (see tray icon)", OverlayForm.Red);
    }

    /// <summary>"Squads (Build)" -> "squads b".</summary>
    static string Short(string mode) => mode.Replace(" (Build)", " b").ToLowerInvariant();

    public static string Duration(TimeSpan t)
    {
        var sec = Math.Max(0, (int)t.TotalSeconds);
        return $"{sec / 3600}:{sec % 3600 / 60:00}:{sec % 60:00}";
    }
}
