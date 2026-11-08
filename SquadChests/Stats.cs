using System.Text;
using System.Text.RegularExpressions;

namespace SquadChests;

/// <summary>Adds up chest counts across matches and prints the report.</summary>
public static class Stats
{
    /// <summary>
    /// What counts as "treasure": pieces of an item's internal name (case-insensitive).
    /// The ultra-rare pirate token / skin drop has never shown up in a replay yet, so its exact
    /// internal name is unknown; these catch the likely names. Lives in data/treasure.txt.
    /// </summary>
    static readonly string[] DefaultTreasurePatterns = ["Token", "Pirate", "Doubloon", "Skin", "Cosmetic", "Voucher"];

    // Defaults written by an earlier version; replaced automatically if the user never edited them.
    static readonly string[] OldDefaultPatterns = ["Sprite_Variant_Jeweled", "TreasureChestSprite"];

    const string TreasureFileHeader = """
        # What counts as "treasure". One piece of an item's internal name per line (not case-sensitive).
        # Lines starting with # are ignored. Save the file and run the tool again to apply.
        #
        # The pirate token / skin drop is ~1 in 100,000 and hasn't appeared in any replay yet, so its exact
        # internal name isn't known. These lines catch the likely names. Separately, the tool always flags
        # any item it has NEVER seen before in your games, so a brand-new rare drop shows up either way.
        # Once you know the real name (it will appear under "never seen before"), put it here.
        #
        # Other examples:
        #   Sprite_Variant_Jeweled   any Jeweled Sprite
        #   TreasureChestSprite      Treasure Chest Sprite
        #
        # Run `app\SquadChests.ReplayReader.exe items --data data` to list every item name seen in your replays.

        """;

    public static string[] LoadTreasurePatterns(string dataDir)
    {
        var file = Path.Combine(dataDir, "treasure.txt");
        string[] lines = File.Exists(file)
            ? File.ReadAllLines(file).Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith('#')).ToArray()
            : [];
        if (lines.Length == 0 || lines.SequenceEqual(OldDefaultPatterns))
        {
            File.WriteAllText(file, TreasureFileHeader + string.Join(Environment.NewLine, DefaultTreasurePatterns) + Environment.NewLine);
            return DefaultTreasurePatterns;
        }
        return lines;
    }

    public static bool IsTreasure(string item, string[] patterns) =>
        patterns.Any(p => item.Contains(p, StringComparison.OrdinalIgnoreCase));

    public static bool IsSprite(string item) => item.StartsWith("[Relic] ESD_") && item.Contains("Sprite");

    /// <summary>"[Relic] ESD_TreasureChestSprite_Variant_Jeweled" -> "Treasure Chest Sprite (Jeweled)".</summary>
    public static string Pretty(string item)
    {
        var s = item.Replace("[Relic] ", "").Replace("[Booty] ", "");
        var jeweled = s.EndsWith("_Variant_Jeweled");
        s = Regex.Replace(s, @"^((ESD|WID|AGID|AID|Athena)_)+", "");
        s = Regex.Replace(s, @"_Variant_\w+$", "");
        s = Regex.Replace(s, @"(?<=[a-z])(?=[A-Z])", " ").Replace('_', ' ');
        if (item.StartsWith("[Booty]")) s = "Booty: " + s;
        return jeweled ? $"{s} (Jeweled)" : s;
    }

    /// <summary>Solo / Duos / Trios / Squads, with " (Build)" for build modes.</summary>
    public static string ModeOf(MatchResult m)
    {
        var p = m.Playlist ?? "";
        var size = p.Contains("Solo") ? "Solo" : p.Contains("Duo") ? "Duos" : p.Contains("Trio") ? "Trios" : p.Contains("Squad") ? "Squads" : "Other";
        return size != "Other" && !p.Contains("NoBuild") ? size + " (Build)" : size;
    }

    /// <summary>
    /// Lobby size at or below which a match counts as a private custom game (you alone, or with a friend or two).
    /// Real matches always have 75+ players and bots.
    /// </summary>
    public const int CustomGameMaxPlayers = 10;

    /// <summary>A private custom match: only you (and maybe a friend or two) in the lobby, no one else.</summary>
    public static bool IsCustom(MatchResult m) => m.PlayerCount is int n && n <= CustomGameMaxPlayers;

    static int ModeOrder(string mode) => mode.StartsWith("Solo") ? 0 : mode.StartsWith("Duo") ? 1 : mode.StartsWith("Trio") ? 2 : mode.StartsWith("Squad") ? 3 : 4;

    /// <summary>
    /// Normal Battle Royale (build and zero build) and Ranked ("Habanero"). Reload, Blitz, LTMs, Creative etc. have other
    /// playlist names and are left out, since their maps and loot differ.
    /// </summary>
    static readonly Regex CountedPlaylist = new(
        @"^Playlist_(NoBuildBR_(Habanero_)?|Habanero_|Default)?(Solo|Duos?|Trios?|Squads?)$", RegexOptions.IgnoreCase);

    public static bool IsCountedMode(MatchResult m) => m.Playlist != null && CountedPlaylist.IsMatch(m.Playlist);

    /// <summary>The season: the main Fortnite version number ("++Fortnite+Release-43.00" -> 43). Each season is one number.</summary>
    public static int? SeasonOf(MatchResult m)
    {
        var x = Regex.Match(m.Version ?? "", @"Release-(\d+)\.");
        return x.Success ? int.Parse(x.Groups[1].Value) : null;
    }

    /// <summary>The newest season among the games read (the one being played now).</summary>
    public static int? CurrentSeason(IEnumerable<MatchResult> all) =>
        Finished(all).Where(m => !IsCustom(m)).Select(SeasonOf).Max();

    /// <summary>Why a game isn't counted, or null if it is.</summary>
    public static string? SkipReason(MatchResult m, int? season) =>
        IsCustom(m) ? "custom game"
        : !IsCountedMode(m) ? "other mode"
        : season != null && SeasonOf(m) is int s && s < season ? "older season"
        : null;

    /// <summary>One entry per counted match (a renamed/saved copy of the same replay is only counted once).</summary>
    public static List<MatchResult> UniqueMatches(IEnumerable<MatchResult> all)
    {
        var list = all as ICollection<MatchResult> ?? all.ToList();
        var season = CurrentSeason(list);
        return Finished(list).Where(m => SkipReason(m, season) == null).ToList();
    }

    /// <summary>Games left out by reason, e.g. "other mode" -> 3.</summary>
    public static Dictionary<string, int> Skipped(IEnumerable<MatchResult> all)
    {
        var list = all as ICollection<MatchResult> ?? all.ToList();
        var season = CurrentSeason(list);
        return Finished(list).Select(m => SkipReason(m, season)).OfType<string>()
            .GroupBy(r => r).ToDictionary(g => g.Key, g => g.Count());
    }

    /// <summary>Playlist names of games left out as "other mode" (so new names can be spotted).</summary>
    public static List<string> OtherModePlaylists(IEnumerable<MatchResult> all) =>
        Finished(all).Where(m => !IsCustom(m) && !IsCountedMode(m)).Select(m => m.Playlist ?? "(unknown)").Distinct().ToList();

    /// <summary>Private custom games that were left out of the counts.</summary>
    public static int CustomGameCount(IEnumerable<MatchResult> all) => Finished(all).Count(IsCustom);

    static IEnumerable<MatchResult> Finished(IEnumerable<MatchResult> all) =>
        all.Where(m => m.Error == null && !m.IsLive && m.OwnerId != null)
           .GroupBy(m => (m.OwnerId, m.Started))
           .Select(g => g.OrderByDescending(m => m.Chests.Count).First())
           .OrderBy(m => m.Started);

    /// <summary>The account that recorded most of the replays (you).</summary>
    static string MainOwner(List<MatchResult> matches) =>
        matches.GroupBy(m => m.OwnerId).OrderByDescending(g => g.Count()).First().Key!;

    // ---------- summary for the app window ----------

    public record ModeStats(string Mode, int Games, int Chests, int Opened, int Destroyed)
    {
        public double PerGame => Games == 0 ? 0 : (double)Chests / Games;
    }

    public record GameStats(MatchResult Match, string Mode, int Chests, int Opened, int Destroyed);

    public class Summary
    {
        public string? PlayerName { get; set; }
        public int Games => All.Count;
        public int Chests => All.Sum(g => g.Chests);
        public int Opened => All.Sum(g => g.Opened);
        public int Destroyed => All.Sum(g => g.Destroyed);
        public double PerGame => Games == 0 ? 0 : (double)Chests / Games;
        public int CustomGames { get; set; }
        public int ChestsSeen { get; set; }
        /// <summary>Counted games, oldest first.</summary>
        public List<GameStats> All { get; } = new();
        public List<ModeStats> Modes { get; set; } = new();
        public List<(MatchResult Match, string Items)> Treasure { get; } = new();
        public GameStats? LastGame => All.Count > 0 ? All[^1] : null;
        public GameStats? BestGame => All.MaxBy(g => g.Chests);
        /// <summary>Season treasures from your own chests, in the games read by a version that counts them.</summary>
        public int Treasures => All.Sum(g => g.Match.OwnTreasures ?? 0);
        /// <summary>Season treasures a teammate found and the squad shared.</summary>
        public int SharedTreasures => All.Sum(g => g.Match.SharedTreasures ?? 0);
        public bool HasTreasureCounts => All.Any(g => g.Match.Treasures != null);

        /// <summary>The latest run of games played back to back (less than 2 hours between them).</summary>
        public List<GameStats> Session()
        {
            var s = new List<GameStats>();
            for (var i = All.Count - 1; i >= 0; i--)
            {
                if (s.Count > 0 && (s[^1].Match.Started - All[i].Match.Started) > TimeSpan.FromHours(2)) break;
                s.Add(All[i]);
            }
            return s;
        }
    }

    public static Summary Summarize(IEnumerable<MatchResult> all, string[] treasure)
    {
        var list = all.ToList();
        var s = new Summary { CustomGames = CustomGameCount(list) };
        var matches = UniqueMatches(list);
        if (matches.Count == 0) return s;
        var meId = MainOwner(matches);
        matches = matches.Where(m => m.OwnerId == meId).ToList();
        s.PlayerName = matches[^1].OwnerName;
        s.ChestsSeen = matches.Sum(m => m.Chests.Count);
        foreach (var m in matches)
        {
            var c = Count(m, treasure);
            s.All.Add(new GameStats(m, ModeOf(m), c.MeOpened + c.MeDestroyed, c.MeOpened, c.MeDestroyed));
            var items = c.TreasureChests.SelectMany(x => x.Items).Concat(c.TreasureInLobby).Where(i => IsTreasure(i, treasure)).Distinct().Select(Pretty).ToList();
            if (items.Count > 0) s.Treasure.Add((m, string.Join(", ", items)));
        }
        s.Modes = s.All.GroupBy(g => g.Mode).OrderBy(g => ModeOrder(g.Key))
            .Select(g => new ModeStats(g.Key, g.Count(), g.Sum(x => x.Chests), g.Sum(x => x.Opened), g.Sum(x => x.Destroyed)))
            .ToList();
        return s;
    }

    /// <summary>Items in this match that never showed up in any other match (Sprites left out). A new rare drop lands here.</summary>
    public static List<string> NewItems(MatchResult m, IEnumerable<MatchResult> others)
    {
        var seen = others.Where(e => e.File != m.File).SelectMany(e => e.LobbyItems.Keys).ToHashSet();
        return seen.Count == 0 ? [] : m.LobbyItems.Keys.Where(i => !seen.Contains(i) && !IsSprite(i)).ToList();
    }

    public record MatchCounts(int MeOpened, int MeDestroyed, int SquadOpened, int SquadDestroyed, List<ChestRecord> TreasureChests, List<string> TreasureInLobby);

    public static MatchCounts Count(MatchResult m, string[] treasure)
    {
        var mine = m.Chests.Where(c => c.ById == m.OwnerId).ToList();
        var squad = m.Chests.Where(c => c.BySquad).ToList();
        return new MatchCounts(
            mine.Count(c => c.Opened), mine.Count(c => c.Destroyed),
            squad.Count(c => c.Opened), squad.Count(c => c.Destroyed),
            m.Chests.Where(c => c.Items.Any(i => IsTreasure(i, treasure))).ToList(),
            m.LobbyItems.Keys.Where(i => IsTreasure(i, treasure)).ToList());
    }

    static int Chests(MatchResult m, string id) => m.Chests.Count(c => c.ById == id);

    public static string Report(IEnumerable<MatchResult> all, string[] treasure, int unreadable)
    {
        var matches = UniqueMatches(all);
        var custom = CustomGameCount(all);
        var sb = new StringBuilder();
        void L(string s = "") => sb.AppendLine(s);

        if (matches.Count == 0)
        {
            L("No finished matches found yet.");
            return sb.ToString();
        }

        var first = matches.First().Started;
        var last = matches.Last().Started;
        var meId = MainOwner(matches);
        var myName = matches.Last(m => m.OwnerId == meId).OwnerName ?? "you";
        matches = matches.Where(m => m.OwnerId == meId).ToList();

        L("==============================================================");
        L($"  FORTNITE CHEST STATS   {matches.Count} matches   {first:MMM d} - {last:MMM d}");
        L("==============================================================");
        if (unreadable > 0) L($"  ({unreadable} replay(s) could not be read and are left out)");
        if (custom > 0) L($"  ({custom} custom game(s) with nobody else in the lobby are left out)");
        var skipped = Skipped(all);
        if (skipped.TryGetValue("other mode", out var om))
            L($"  ({om} game(s) in other modes are left out: {string.Join(", ", OtherModePlaylists(all).Select(p => p.Replace("Playlist_", "")))})");
        if (skipped.TryGetValue("older season", out var os))
            L($"  ({os} game(s) from an older season are left out; counting season v{CurrentSeason(all)})");
        L();

        // ---------- you ----------
        var myChests = matches.SelectMany(m => m.Chests.Where(c => c.ById == meId)).ToList();
        var myOpened = myChests.Where(c => c.Opened).ToList();
        var myDestroyed = myChests.Where(c => c.Destroyed).ToList();
        L($"YOU ({myName})");
        L($"  Chests ................ {myChests.Count}   = {(double)myChests.Count / matches.Count:F1} per game");
        L($"    opened .............. {myOpened.Count}   = {(double)myOpened.Count / matches.Count:F1} per game");
        foreach (var g in myOpened.GroupBy(c => c.Kind).OrderByDescending(g => g.Count()))
            L($"        {g.Key,-22} {g.Count()}");
        L($"    destroyed ........... {myDestroyed.Count}   = {(double)myDestroyed.Count / matches.Count:F1} per game");
        foreach (var g in myDestroyed.GroupBy(c => c.DestroyedWith ?? (c.ByIsGuess ? "Up close (best guess)" : "Hit")).OrderByDescending(g => g.Count()))
            L($"        {g.Key,-22} {g.Count()}");
        L();

        // ---------- season treasure ----------
        var tKnown = matches.Where(m => m.Treasures != null).ToList();
        if (tKnown.Count > 0)
        {
            var tOwn = tKnown.Sum(m => m.OwnTreasures!.Value);
            var tShared = tKnown.Sum(m => m.SharedTreasures!.Value);
            var tChests = tKnown.Sum(m => Chests(m, meId));
            L($"SEASON TREASURE");
            L($"  From your chests ...... {tOwn}   in {tKnown.Count(m => m.OwnTreasures > 0)} of {tKnown.Count} games"
              + (tOwn > 0 ? $"   = 1 per {(double)tChests / tOwn:F0} of your chests" : ""));
            foreach (var g in tKnown.GroupBy(ModeOf).OrderBy(g => ModeOrder(g.Key)))
                L($"        {g.Key,-22} {g.Sum(m => m.OwnTreasures!.Value)} in {g.Count()} games");
            var best = tKnown.MaxBy(m => m.OwnTreasures!.Value)!;
            if (best.OwnTreasures > 1) L($"    most in one game .... {best.OwnTreasures}   ({best.Started:MMM d HH:mm}, {ModeOf(best).ToLower()})");
            L($"  Shared by teammates ... {tShared}   (a teammate's chest gave it; the squad shares prizes)");
            L("  The replay doesn't say which prize it was. Awards other than gizmos drop only");
            L("  once each, so nearly all of yours are gizmos: subtract the awards you know you got.");
            if (tKnown.Count < matches.Count)
                L($"  ({matches.Count - tKnown.Count} older game(s) aren't included: Fortnite already deleted the replay.)");
            L();
        }

        // ---------- by mode ----------
        L("BY MODE  (your chests per game)");
        L($"  {"Mode",-14} {"Games",5}   {"Chests",6}   {"Opened",6}   {"Destroyed",9}");
        foreach (var g in matches.GroupBy(ModeOf).OrderBy(g => ModeOrder(g.Key)))
        {
            var games = g.Count();
            var mine = g.SelectMany(m => m.Chests.Where(c => c.ById == meId)).ToList();
            L($"  {g.Key,-14} {games,5}   {(double)mine.Count / games,6:F1}   {(double)mine.Count(c => c.Opened) / games,6:F1}   {(double)mine.Count(c => c.Destroyed) / games,9:F1}");
        }
        L();

        // ---------- teammates ----------
        var mates = new Dictionary<string, Mate>();
        foreach (var m in matches)
        {
            foreach (var p in m.Squad.Where(s => s.Id != meId))
            {
                if (!mates.TryGetValue(p.Id, out var mt)) mates[p.Id] = mt = new Mate { Name = p.Name, Bot = p.IsBot };
                mt.Name = p.Name;
                mt.Games++;
                mt.YouWithThem += Chests(m, meId);
                if (m.TeamPlacement == 1) mt.Wins++;
                var mode = ModeOf(m);
                mt.Modes[mode] = mt.Modes.GetValueOrDefault(mode) + 1;
            }
        }
        var repeat = mates.Values.Where(x => x.Games >= 2).OrderByDescending(x => x.Games).ThenBy(x => x.Name).ToList();
        var once = mates.Values.Count(x => x.Games < 2);
        L("TEAMMATES YOU PLAYED WITH 2+ TIMES");
        L($"  {"Player",-20} {"Games",5} {"Wins",5}   {"Your chests/game",16}   Modes");
        foreach (var x in repeat)
        {
            var name = x.Name + (x.Bot ? " [bot]" : "");
            if (name.Length > 20) name = name[..19] + "~";
            var modes = string.Join(", ", x.Modes.OrderBy(k => ModeOrder(k.Key)).Select(k => $"{k.Value} {k.Key.ToLower()}"));
            L($"  {name,-20} {x.Games,5} {x.Wins,5}   {(double)x.YouWithThem / x.Games,16:F1}   {modes}");
        }
        if (once > 0) L($"  + {once} randoms/fills you only played with once");
        L("  Your chests/game = how many YOU got in the games you played with them.");
        L("  Teammates' own chests aren't counted: your replay only records what happens");
        L("  near you, so most of what they loot never ends up in your file.");
        L();

        // ---------- treasure ----------
        var counts = matches.Select(m => (Match: m, C: Count(m, treasure))).ToList();
        var drops = counts.SelectMany(x => x.C.TreasureChests.Select(c => (x.Match, Chest: c))).ToList();
        var lobbies = counts.Where(x => x.C.TreasureInLobby.Count > 0).ToList();
        L("TREASURE  (pirate token / skin; matches: " + string.Join(", ", treasure) + ")");
        if (drops.Count == 0 && lobbies.Count == 0)
        {
            L($"  None seen in your {matches.Count} games yet. (It's ~1 in 100,000 chests; your replays");
            L($"  saw about {matches.Sum(m => m.Chests.Count):N0} chests in total.)");
        }
        else
        {
            L($"  Seen in {lobbies.Count} lobby(s), came out of a chest {drops.Count} time(s).");
            foreach (var (m, c) in drops)
            {
                var items = string.Join(", ", c.Items.Where(i => IsTreasure(i, treasure)).Select(Pretty));
                L($"    {m.Started:MMM d HH:mm}  {items}  - {c.Kind} {(c.Opened ? "opened" : "destroyed")} by {c.ByName ?? "someone"}{(c.BySquad ? " (your squad)" : "")}");
            }
            foreach (var x in lobbies.Where(l => !drops.Any(d => d.Match == l.Match)))
                L($"    {x.Match.Started:MMM d HH:mm}  {string.Join(", ", x.C.TreasureInLobby.Select(Pretty))}  - in the lobby (not from a chest near you)");
        }

        // Rarest things: items that showed up in exactly one game. A new ultra-rare drop would land here.
        var itemGames = matches.SelectMany(m => m.LobbyItems.Keys.Select(i => (Item: i, Match: m)))
            .Where(x => !IsSprite(x.Item))
            .GroupBy(x => x.Item).Where(g => g.Count() == 1).Select(g => g.First()).OrderBy(x => x.Match.Started).ToList();
        if (itemGames.Count > 0)
        {
            L();
            L("  Rarest items seen (only ever in ONE of your games):");
            foreach (var (item, m) in itemGames)
            {
                var chest = m.Chests.FirstOrDefault(c => c.Items.Contains(item));
                var from = chest != null ? $"from a {chest.Kind.ToLower()} {(chest.Opened ? "opened" : "destroyed")} by {chest.ByName ?? "someone"}" : "in the lobby";
                L($"    {m.Started:MMM d HH:mm}  {Pretty(item),-30} {from}");
            }
        }
        L("  (Items are matched to the chest they flew out of; usually right, not 100%.)");
        return sb.ToString();
    }

    class Mate
    {
        public string Name = "";
        public bool Bot;
        public int Games, Wins, YouWithThem;
        public Dictionary<string, int> Modes = new();
    }

    /// <summary>One line for watch mode after a game ends. <paramref name="earlier"/> = all other matches, to spot never-seen items.</summary>
    public static string NewMatchLine(MatchResult m, string[] treasure, IEnumerable<MatchResult> earlier)
    {
        if (IsCustom(m)) return $"{m.Started:HH:mm}  custom game (nobody else in the lobby), not counted";
        if (SkipReason(m, CurrentSeason(earlier.Append(m))) is string why) return $"{m.Started:HH:mm}  {why} ({m.Playlist}), not counted";
        var c = Count(m, treasure);
        var brandNew = NewItems(m, earlier);

        var sb = new StringBuilder($"{m.Started:HH:mm}  {ModeOf(m)}: you +{c.MeOpened + c.MeDestroyed} chests ({c.MeOpened} opened, {c.MeDestroyed} destroyed), placed #{m.TeamPlacement}");
        if (c.TreasureChests.Count > 0)
            sb.Append($"\n        *** TREASURE: {string.Join(", ", c.TreasureChests.SelectMany(x => x.Items).Where(i => IsTreasure(i, treasure)).Select(Pretty))} ***");
        else if (c.TreasureInLobby.Count > 0)
            sb.Append($"\n        *** TREASURE in the lobby: {string.Join(", ", c.TreasureInLobby.Select(Pretty))} ***");
        if (brandNew.Count > 0)
            sb.Append($"\n        NEVER SEEN BEFORE: {string.Join(", ", brandNew.Select(i => $"{Pretty(i)} [{i}]"))}");
        return sb.ToString();
    }

    public static void WriteCsv(IEnumerable<MatchResult> all, string[] treasure, string path)
    {
        var sb = new StringBuilder("date,mode,playlist,placement,you_opened,you_destroyed,you_total,season_treasures,shared_treasures,treasure_from_chests,treasure_in_lobby,squad,file\n");
        foreach (var m in UniqueMatches(all))
        {
            var c = Count(m, treasure);
            string Q(string s) => "\"" + s.Replace("\"", "\"\"") + "\"";
            sb.AppendLine(string.Join(",",
                m.Started?.ToString("yyyy-MM-dd HH:mm"), ModeOf(m), m.Playlist, m.TeamPlacement,
                c.MeOpened, c.MeDestroyed, c.MeOpened + c.MeDestroyed, m.OwnTreasures?.ToString() ?? "", m.SharedTreasures?.ToString() ?? "",
                Q(string.Join("; ", c.TreasureChests.SelectMany(x => x.Items).Where(i => IsTreasure(i, treasure)).Select(Pretty))),
                Q(string.Join("; ", c.TreasureInLobby.Select(Pretty))),
                Q(string.Join("; ", m.Squad.Select(s => s.Name))), m.File));
        }
        File.WriteAllText(path, sb.ToString());
    }
}
