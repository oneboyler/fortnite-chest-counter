using SquadChests;

string? Arg(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
var replayDir = Arg("--replays") ?? Scanner.DefaultReplayDir();
var dataDir = Arg("--data") ?? Path.Combine(AppContext.BaseDirectory, "data");
var cmd = args.Length > 0 && !args[0].StartsWith("--") ? args[0] : "stats";

switch (cmd)
{
    case "discover":
        DiscoverReader.Run(args[1], args.Length >= 3 ? args[2] : "discover.txt");
        break;

    case "search":
        DiscoverReader.Search(replayDir, args.Length >= 2 && !args[1].StartsWith("--") ? args[1] : ".");
        break;

    case "rarepaths":
        DiscoverReader.RarePaths(replayDir, args.Length >= 2 && int.TryParse(args[1], out var mc) ? mc : 3);
        break;

    case "abilities":
        DiscoverReader.Abilities(args[1], args.Contains("--probe"));
        break;

    case "when":
        DiscoverReader.When(args[1], args[2]);
        break;

    case "cooccur":
        DiscoverReader.CoOccur(replayDir, args[1]);
        break;

    case "dump":
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var r = LootReader.Read(args[1]);
        Console.WriteLine($"{r.File}  ({sw.Elapsed.TotalSeconds:F1}s)  live={r.IsLive}  {r.Playlist}  {r.Version}  placement={r.TeamPlacement}");
        if (r.Error != null) Console.WriteLine("ERROR " + r.Error);
        foreach (var w in r.Warnings) Console.WriteLine("WARN " + w);
        Console.WriteLine($"Owner: {r.OwnerName} ({r.OwnerId})");
        foreach (var s in r.Squad) Console.WriteLine($"  squad: {s.Name} {(s.IsBot ? "[bot]" : "")} {s.Id}");
        Console.WriteLine($"Chest events: {r.Chests.Count}");
        foreach (var c in r.Chests)
        {
            var what = c.Opened ? "OPENED" : "DESTROYED";
            Console.WriteLine($"  {TimeSpan.FromSeconds(c.Time):mm\\:ss} {what,-9} {c.Kind,-15} tier={c.Tier} by={c.ByName ?? "?"}{(c.ByIsGuess ? "(guess)" : "")}{(c.DestroyedWith != null ? " via " + c.DestroyedWith : "")}{(c.BySquad ? " [SQUAD]" : "")}{(c.AlreadyOpenWhenSeen ? " (already open when seen)" : "")}  items=[{string.Join(", ", c.Items)}]");
        }
        Console.WriteLine($"Treasure times: {string.Join(" ", r.TreasureTimes ?? [])}");
        foreach (var p in r.SpecialPrizes ?? [])
            Console.WriteLine($"Special prize: {TimeSpan.FromSeconds(p.Time):mm\\:ss} {(p.Variant == "" ? "plain" : p.Variant)} tier={p.Tier} success={p.Success} by={p.ByName ?? "?"}{(p.BySquad ? " [SQUAD]" : "")}");
        if (args.Contains("--items"))
        {
            Console.WriteLine("Lobby items:");
            foreach (var kv in r.LobbyItems.OrderByDescending(k => k.Value)) Console.WriteLine($"  {kv.Value,4}  {kv.Key}");
        }
        break;
    }

    case "games":
    {
        // Every match with lobby size, to check which ones count as custom games.
        var scanner = new Scanner(replayDir, dataDir);
        scanner.ScanAll(skipInProgress: true, Console.WriteLine);
        foreach (var m in scanner.Matches.Values.Where(m => m.Error == null).OrderBy(m => m.Started))
            Console.WriteLine($"{m.Started:MMM dd HH:mm}  players={m.PlayerCount,3} humans={m.HumanCount,3} max={m.MaxPlayers,3}  {(Stats.IsCustom(m) ? "CUSTOM" : "      ")}  {Stats.ModeOf(m),-8} {m.Playlist}  chests={m.Chests.Count(c => c.ById == m.OwnerId)}  squad={string.Join(",", m.Squad.Select(s => s.Name))}");
        break;
    }

    case "items":
    {
        // Research helper: which items show up across all matches, how rare they are, and how often they come out of chests.
        var scanner = new Scanner(replayDir, dataDir);
        scanner.ScanAll(skipInProgress: true, Console.WriteLine);
        var ok = scanner.Matches.Values.Where(m => m.Error == null).ToList();
        var inMatches = ok.SelectMany(m => m.LobbyItems.Keys).GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        var fromChests = ok.SelectMany(m => m.Chests.SelectMany(c => c.Items)).GroupBy(k => k).ToDictionary(g => g.Key, g => g.Count());
        Console.WriteLine($"{ok.Count} matches. item | matches it appeared in | times dropped from chests");
        foreach (var kv in inMatches.OrderBy(k => k.Value))
            Console.WriteLine($"  {kv.Key,-60} {kv.Value,4} {fromChests.GetValueOrDefault(kv.Key),6}");
        break;
    }

    case "stats":
    case "watch":
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        var watcher = new GameWatcher(replayDir, dataDir);
        var scanner = watcher.Scanner;
        var treasure = Stats.LoadTreasurePatterns(dataDir);
        Console.WriteLine($"Replays: {replayDir}");
        scanner.ScanAll(skipInProgress: true, Console.WriteLine);
        Console.WriteLine();
        Console.Write(Stats.Report(scanner.Matches.Values, treasure, scanner.Matches.Values.Count(m => m.Error != null)));
        Stats.WriteCsv(scanner.Matches.Values, treasure, Path.Combine(dataDir, "matches.csv"));
        if (cmd == "stats") break;

        // ---- watch: wait for new games ----
        Console.WriteLine();
        Console.WriteLine("Watching for new games... (leave this open while you play, Ctrl+C to stop)");
        var lastStatus = "";
        watcher.Changed += () =>
        {
            if (watcher.Status == lastStatus) return;
            lastStatus = watcher.Status;
            if (lastStatus == GameWatcher.InGameStatus) Console.WriteLine($"{DateTime.Now:HH:mm}  game in progress, will count it when it ends...");
        };
        watcher.GameAdded += r =>
        {
            var all = watcher.Snapshot;
            Stats.WriteCsv(all, treasure, Path.Combine(dataDir, "matches.csv"));
            var s = Stats.Summarize(all, treasure);
            Console.WriteLine(Stats.NewMatchLine(r, treasure, all));
            Console.WriteLine($"        TOTAL  you {s.Chests} chests in {s.Games} matches ({s.PerGame:F1} per game) | treasure {s.Treasure.Count}");
        };
        watcher.Start(initialScan: false);
        Thread.Sleep(Timeout.Infinite);
        break;
    }

    default:
        Console.WriteLine("usage: [stats] | watch | games | dump <file.replay> [--items] | items | discover <file.replay> [out.txt]");
        Console.WriteLine("       options: --replays <folder>  --data <folder>");
        break;
}
