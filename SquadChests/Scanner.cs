using System.Collections.Concurrent;
using System.Text.Json;

namespace SquadChests;

/// <summary>Finds replays, parses the ones not in the cache yet, and keeps results in data/matches.json.</summary>
public class Scanner
{
    public string ReplayDir { get; }
    public string DataDir { get; }
    string CachePath => Path.Combine(DataDir, "matches.json");

    /// <summary>Results by replay file name. Kept even after Fortnite deletes the replay, so totals keep growing.</summary>
    public Dictionary<string, MatchResult> Matches { get; private set; } = new();

    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public Scanner(string replayDir, string dataDir)
    {
        ReplayDir = replayDir;
        DataDir = dataDir;
        Directory.CreateDirectory(DataDir);
        if (File.Exists(CachePath))
        {
            try { Matches = JsonSerializer.Deserialize<Dictionary<string, MatchResult>>(File.ReadAllText(CachePath)) ?? new(); }
            catch (Exception ex) { Console.WriteLine($"Could not read cache ({ex.Message}), starting fresh."); }
        }
    }

    public static string DefaultReplayDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");

    public void Save()
    {
        var tmp = CachePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(Matches, Json));
        File.Move(tmp, CachePath, overwrite: true);
    }

    /// <summary>True if this replay still needs reading (new, or changed since we last read it).</summary>
    public bool NeedsRead(FileInfo f) =>
        !Matches.TryGetValue(f.Name, out var m) || m.Size != f.Length || m.LastWrite != f.LastWriteTimeUtc
        || m.ReaderVersion < MatchResult.CurrentReaderVersion;

    /// <summary>Reads every replay in the folder that isn't cached yet. Returns how many were read.</summary>
    public int ScanAll(bool skipInProgress, Action<string>? log = null, Action<int, int>? progress = null)
    {
        if (!Directory.Exists(ReplayDir))
        {
            log?.Invoke($"Replay folder not found: {ReplayDir}");
            return 0;
        }

        var todo = new DirectoryInfo(ReplayDir).GetFiles("*.replay")
            .Where(NeedsRead)
            .Where(f => !skipInProgress || !LooksInProgress(f))
            .OrderBy(f => f.LastWriteTimeUtc)
            .ToList();
        if (todo.Count == 0) return 0;

        log?.Invoke($"Reading {todo.Count} replay(s)...");
        progress?.Invoke(0, todo.Count);
        var done = 0;
        var results = new ConcurrentBag<MatchResult>();
        var sw = System.Diagnostics.Stopwatch.StartNew();
        Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount - 1, 1, 6) }, f =>
        {
            var r = LootReader.Read(f.FullName);
            results.Add(r);
            var n = Interlocked.Increment(ref done);
            progress?.Invoke(n, todo.Count);
            if (r.Error != null) log?.Invoke($"  [{n}/{todo.Count}] {f.Name}: could not read ({r.Error})");
            else if (n % 10 == 0 || n == todo.Count) log?.Invoke($"  [{n}/{todo.Count}] {sw.Elapsed.TotalSeconds:F0}s");
        });

        foreach (var r in results) Matches[r.File] = r;
        Save();
        return todo.Count;
    }

    /// <summary>
    /// A replay is still being written while Fortnite is in that match. We treat it as in progress if it was
    /// modified in the last 90 seconds; the replay's own "live" flag is checked after reading as well.
    /// </summary>
    public static bool LooksInProgress(FileInfo f) =>
        DateTime.UtcNow - f.LastWriteTimeUtc < TimeSpan.FromSeconds(5) || IsLocked(f);

    /// <summary>
    /// Fortnite keeps the replay open for writing until you leave the match. As soon as it lets go, the game is over
    /// and the file can be read (no need to wait for it to sit unchanged for a while).
    /// </summary>
    static bool IsLocked(FileInfo f)
    {
        try
        {
            using var s = new FileStream(f.FullName, FileMode.Open, FileAccess.Read, FileShare.Read);
            return false;
        }
        catch (IOException) { return true; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
