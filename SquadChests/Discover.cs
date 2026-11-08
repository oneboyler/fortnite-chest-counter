using System.Text.RegularExpressions;
using FortniteReplayReader;
using FortniteReplayReader.Models.NetFieldExports;
using Unreal.Core.Contracts;
using Unreal.Core.Models;
using Unreal.Core.Models.Enums;

namespace SquadChests;

/// <summary>Debug helper: lists every actor class and export group in one replay so we can find chest/treasure data.</summary>
public class DiscoverReader : ReplayReader
{
    public Dictionary<string, int> ArchetypeCounts { get; } = new();
    public Dictionary<string, int> StaticActorCounts { get; } = new();
    public List<string> ExportGroupLines { get; } = new();
    public Dictionary<string, int> ExportReadCounts { get; } = new();

    public DiscoverReader(ParseMode mode) : base(null, mode) { }

    // Debug mode throws on event types the parser does not know; the event bytes are already consumed, so skipping is safe.
    public override void ReadEvent(Unreal.Core.FArchive archive)
    {
        try { base.ReadEvent(archive); } catch (Unreal.Core.Exceptions.UnknownEventException) { }
    }

    static readonly Regex Suffix = new(@"(_UAID_[0-9A-F]+|_\d+)+$", RegexOptions.Compiled);

    // ---- "when": replay time at which matching paths first show up ----
    float _frameTime;
    int _seen;
    public Regex? Watch { get; set; }
    public List<(float Time, uint Guid, string Path)> FirstSeen { get; } = new();

    public override void ReadDemoFrameIntoPlaybackPackets(Unreal.Core.FArchive archive)
    {
        var pos = archive.Position;
        if (archive.NetworkVersion >= NetworkVersionHistory.HISTORY_MULTIPLE_LEVELS) archive.ReadInt32();
        _frameTime = archive.ReadSingle();
        archive.Seek(pos, SeekOrigin.Begin);
        base.ReadDemoFrameIntoPlaybackPackets(archive);
        if (Watch == null) return;
        var map = _netGuidCache.NetGuidToPathName;
        if (map.Count == _seen) return;
        var i = 0;
        foreach (var kv in map)
        {
            if (i++ >= _seen && Watch.IsMatch(kv.Value)) FirstSeen.Add((_frameTime, kv.Key, kv.Value));
        }
        _seen = map.Count;
    }

    public static void When(string file, string pattern)
    {
        var reader = new DiscoverReader(ParseMode.Minimal) { Watch = new Regex(pattern, RegexOptions.IgnoreCase) };
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        reader.ReadReplay(fs);
        foreach (var (t, guid, p) in reader.FirstSeen) Console.WriteLine($"{TimeSpan.FromSeconds(t):mm\\:ss}  guid={guid}  {p}");
    }

    // ---- "abilities": every granted-ability change the replay owner received ----
    public List<string> AbilityLines { get; } = new();

    string PathOf(uint? guid) => guid is uint g && _netGuidCache.NetGuidToPathName.TryGetValue(g, out var p) ? p : guid?.ToString() ?? "-";

    protected override void OnNetDeltaRead(uint channelIndex, Unreal.Core.Models.NetDeltaUpdate update)
    {
        base.OnNetDeltaRead(channelIndex, update);
        if (update.Export is AbilitySpecExport a)
        {
            var name = PathOf(a.Ability);
            if (a.Ability != null) _abilityAt[(channelIndex, update.ElementIndex)] = name;
            AbilityLines.Add($"{TimeSpan.FromSeconds(_frameTime):mm\\:ss}  ch={channelIndex} idx={update.ElementIndex} handle={a.Handle} level={a.Level} input={a.InputID}  ability={name}  source={PathOf(a.SourceObject)}");
        }
        else if (update.Export == null && _abilityAt.Remove((channelIndex, update.ElementIndex), out var gone))
        {
            AbilityLines.Add($"{TimeSpan.FromSeconds(_frameTime):mm\\:ss}  ch={channelIndex} idx={update.ElementIndex} REMOVED  ability={gone}");
        }
    }

    readonly Dictionary<(uint, int), string> _abilityAt = new();

    int _probeCount;
    public bool ProbeFields { get; set; }
    public Dictionary<string, int> DeltaGroups { get; } = new();

    // Research probe: log the raw field handles of ability-list elements (read twice: with and without a checksum bit).
    public override bool ReceiveProperties(Unreal.Core.FBitArchive archive, Unreal.Core.Models.NetFieldExportGroup group, uint channelIndex,
        out INetFieldExportGroup? exportGroup, bool enablePropertyChecksum = true, bool netDeltaUpdate = false)
    {
        if (netDeltaUpdate && ProbeFields) DeltaGroups[group.PathName] = DeltaGroups.GetValueOrDefault(group.PathName) + 1;
        if (netDeltaUpdate && ProbeFields && group.PathName.EndsWith("FortAbilitySystemComponentAthena"))
        {
            _probeCount++;
            foreach (var skipBit in new[] { false })
            {
                var pos = archive.Position;
                var sb = new System.Text.StringBuilder($"{TimeSpan.FromSeconds(_frameTime):mm\\:ss} PROBE ch={channelIndex} left={archive.GetBitsLeft()}: ");
                for (var i = 0; i < 14; i++)
                {
                    var h = archive.ReadIntPacked();
                    if (h == 0) { sb.Append("END"); break; }
                    h--;
                    var name = group.IsValidIndex(h) ? group.NetFieldExports[(int)h]?.Name ?? "null" : "INVALID";
                    if (name == "INVALID") { sb.Append($"{h}:INVALID"); break; }
                    var nb = archive.ReadIntPacked();
                    sb.Append($"{h}:{name}/{nb}");
                    if (nb > (uint)archive.GetBitsLeft()) { sb.Append(" TOO-LONG"); break; }
                    if (nb > 0 && nb <= 256 && name is not ("HandleValue" or "ability" or "SourceObject"))
                    {
                        var bytes = archive.ReadBits((int)nb);
                        sb.Append('=').Append(Convert.ToHexString(bytes));
                    }
                    else archive.SkipBits((int)nb);
                    sb.Append(' ');
                }
                AbilityLines.Add(sb.ToString());
                archive.Seek(pos, SeekOrigin.Begin);
            }
        }
        return base.ReceiveProperties(archive, group, channelIndex, out exportGroup, enablePropertyChecksum, netDeltaUpdate);
    }

    public static void Abilities(string file, bool probe = false)
    {
        var reader = new DiscoverReader(ParseMode.Debug) { ProbeFields = probe };
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        reader.ReadReplay(fs);
        Console.WriteLine($"{reader.AbilityLines.Count} ability updates");
        foreach (var l in reader.AbilityLines) Console.WriteLine(l);
        foreach (var kv in reader.DeltaGroups) Console.WriteLine($"DELTA {kv.Value} {kv.Key}");
    }

    protected override void OnChannelOpened(uint channelIndex, NetworkGUID? actor)
    {
        base.OnChannelOpened(channelIndex, actor);
        var a = Channels[channelIndex]?.Actor;
        if (a == null) return;
        if (a.Archetype?.Value is uint arch && _netGuidCache.TryGetPathName(arch, out var path))
        {
            ArchetypeCounts[path] = ArchetypeCounts.GetValueOrDefault(path) + 1;
        }
        else if (_netGuidCache.TryGetPathName(a.ActorNetGUID.Value, out var spath))
        {
            var key = Suffix.Replace(spath, "");
            StaticActorCounts[key] = StaticActorCounts.GetValueOrDefault(key) + 1;
        }
    }

    protected override void OnExportRead(uint channelIndex, INetFieldExportGroup? exportGroup)
    {
        base.OnExportRead(channelIndex, exportGroup);
        if (exportGroup != null)
        {
            var n = exportGroup.GetType().Name;
            ExportReadCounts[n] = ExportReadCounts.GetValueOrDefault(n) + 1;
        }
    }

    protected override void Cleanup()
    {
        // snapshot every asset/object path the replay referenced (for `search`)
        if (AllPaths.Count == 0)
        {
            foreach (var p in _netGuidCache.NetGuidToPathName.Values) AllPaths.Add(p);
            foreach (var p in _netGuidCache.NetFieldExportGroupMap.Keys) AllPaths.Add(p);
            foreach (var p in ArchetypeCounts.Keys) AllPaths.Add(p);
            // gameplay tag names (prize types etc. may only show up as tags)
            foreach (var kv in _netGuidCache.NetFieldExportGroupMap.Where(k => k.Key.Contains("GameplayTag")))
                foreach (var f in kv.Value.NetFieldExports.Where(f => f != null)) AllPaths.Add("tag:" + f!.Name);
        }
        // snapshot export groups before the base class wipes the cache
        if (ExportGroupLines.Count == 0)
        {
            foreach (var kv in _netGuidCache.NetFieldExportGroupMap.OrderBy(k => k.Key))
            {
                var fields = string.Join(",", kv.Value.NetFieldExports.Where(f => f != null).Select(f => f!.Name));
                ExportGroupLines.Add($"{kv.Key}  [{fields}]");
            }
        }
        base.Cleanup();
    }

    public HashSet<string> AllPaths { get; } = new();

    /// <summary>Research helper: every referenced path matching a regex, across all replays, with how many replays had it.</summary>
    public static void Search(string replayDir, string pattern)
    {
        var re = new Regex(pattern, RegexOptions.IgnoreCase);
        var counts = new System.Collections.Concurrent.ConcurrentDictionary<string, int>();
        var files = Directory.GetFiles(replayDir, "*.replay");
        var totalPaths = new System.Collections.Concurrent.ConcurrentDictionary<string, byte>();
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 6) }, f =>
        {
            try
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var reader = new DiscoverReader(ParseMode.Minimal);
                reader.ReadReplay(fs);
                foreach (var p in reader.AllPaths)
                {
                    totalPaths.TryAdd(p, 0);
                    if (re.IsMatch(p)) counts.AddOrUpdate(p, 1, (_, v) => v + 1);
                }
            }
            catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(f)}: {ex.Message}"); }
        });
        Console.WriteLine($"{files.Length} replays, {totalPaths.Count} distinct paths, {counts.Count} match /{pattern}/  (count = replays it appeared in)");
        foreach (var kv in counts.OrderBy(k => k.Value).ThenBy(k => k.Key)) Console.WriteLine($"{kv.Value,4}  {kv.Key}");
    }

    /// <summary>Research helper: for each replay, the paths that only a few replays (at most maxCount) referenced.</summary>
    public static void RarePaths(string replayDir, int maxCount)
    {
        var perFile = new System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>>();
        var files = Directory.GetFiles(replayDir, "*.replay");
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 6) }, f =>
        {
            try
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var reader = new DiscoverReader(ParseMode.Minimal);
                reader.ReadReplay(fs);
                perFile[Path.GetFileName(f)] = reader.AllPaths.Select(p => Suffix.Replace(p, "")).ToHashSet();
            }
            catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(f)}: {ex.Message}"); }
        });
        var counts = perFile.Values.SelectMany(s => s).GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
        foreach (var (file, paths) in perFile.OrderBy(k => k.Key))
            foreach (var p in paths.Where(p => counts[p] <= maxCount).OrderBy(p => p))
                Console.WriteLine($"{file}\t{counts[p]}\t{p}");
    }

    /// <summary>Research helper: paths that only ever appear in replays that also contain a path matching the pattern.</summary>
    public static void CoOccur(string replayDir, string pattern)
    {
        var re = new Regex(pattern, RegexOptions.IgnoreCase);
        var perFile = new System.Collections.Concurrent.ConcurrentDictionary<string, HashSet<string>>();
        var files = Directory.GetFiles(replayDir, "*.replay");
        Parallel.ForEach(files, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 6) }, f =>
        {
            try
            {
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                var reader = new DiscoverReader(ParseMode.Minimal);
                reader.ReadReplay(fs);
                perFile[Path.GetFileName(f)] = reader.AllPaths.Select(p => Suffix.Replace(p, "")).ToHashSet();
            }
            catch (Exception ex) { Console.WriteLine($"{Path.GetFileName(f)}: {ex.Message}"); }
        });
        var with = perFile.Where(kv => kv.Value.Any(re.IsMatch)).Select(kv => kv.Key).ToHashSet();
        Console.WriteLine($"{with.Count} of {perFile.Count} replays match /{pattern}/:");
        foreach (var f in with.OrderBy(f => f)) Console.WriteLine("  " + f);
        var inWith = perFile.Where(kv => with.Contains(kv.Key)).SelectMany(kv => kv.Value).GroupBy(p => p).ToDictionary(g => g.Key, g => g.Count());
        var inWithout = perFile.Where(kv => !with.Contains(kv.Key)).SelectMany(kv => kv.Value).ToHashSet();
        Console.WriteLine("paths only in those replays (count):");
        foreach (var kv in inWith.Where(kv => !inWithout.Contains(kv.Key) && kv.Key.StartsWith('/')).OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
            Console.WriteLine($"{kv.Value,4}  {kv.Key}");
    }

    public static void Run(string file, string outFile)
    {
        var reader = new DiscoverReader(ParseMode.Debug);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        var replay = reader.ReadReplay(file);
        Console.WriteLine($"Parsed {Path.GetFileName(file)} in {sw.Elapsed.TotalSeconds:F1}s  branch={replay.Header.Branch}");
        Console.WriteLine($"Playlist={replay.GameData?.CurrentPlaylist}  players={replay.PlayerData?.Count()}  teams={replay.TeamData?.Count()} recorder={replay.GameData?.RecorderId}");
        Console.WriteLine($"Stats: elims={replay.Stats?.Eliminations} teamPlacement={replay.TeamStats?.Position}");

        using var w = new StreamWriter(outFile);
        w.WriteLine("### PLAYERS (name | team | owner | bot | epicId | placement | kills | locs)");
        foreach (var p in replay.PlayerData ?? [])
            w.WriteLine($"{p.PlayerName} | {p.TeamIndex} | {p.IsReplayOwner} | {p.IsBot} | {p.EpicId} | {p.Placement} | {p.Kills} | {p.Locations.Count}");
        w.WriteLine();
        w.WriteLine("### EXPORTS READ (type count)");
        foreach (var kv in reader.ExportReadCounts.OrderByDescending(k => k.Value)) w.WriteLine($"{kv.Value,7}  {kv.Key}");
        w.WriteLine();
        w.WriteLine("### STATIC (MAP-PLACED) ACTORS (count  path)");
        foreach (var kv in reader.StaticActorCounts.OrderBy(k => k.Key)) w.WriteLine($"{kv.Value,6}  {kv.Key}");
        w.WriteLine();
        w.WriteLine("### SPAWNED ACTOR ARCHETYPES (count  path)");
        foreach (var kv in reader.ArchetypeCounts.OrderBy(k => k.Key)) w.WriteLine($"{kv.Value,6}  {kv.Key}");
        w.WriteLine();
        w.WriteLine("### NET FIELD EXPORT GROUPS");
        foreach (var l in reader.ExportGroupLines) w.WriteLine(l);
        Console.WriteLine($"Wrote {outFile}");
    }
}
