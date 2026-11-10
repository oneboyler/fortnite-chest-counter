using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquadChests;

/// <summary>Finds replays, parses the ones not in the cache yet, and keeps results in data/matches.json.</summary>
public class Scanner
{
    public string ReplayDir { get; }
    public string DataDir { get; }
    string CachePath => Path.Combine(DataDir, "matches.json");
    /// <summary>matches.json as it was one save earlier. Used when matches.json is damaged.</summary>
    string BackupPath => CachePath + ".bak";

    /// <summary>Results by replay file name. Kept even after Fortnite deletes the replay, so totals keep growing.</summary>
    public Dictionary<string, MatchResult> Matches { get; private set; } = new();

    /// <summary>Set when matches.json couldn't be read on start: what happened and where the damaged file was kept.</summary>
    public string? LoadProblem { get; private set; }

    /// <summary>matches.json couldn't be read AND couldn't be moved out of the way: saving would destroy it, so don't.</summary>
    bool _saveBlocked;

    // Values that are null, false or 0 are left out of the file (a lot smaller); they read back the same.
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault };

    /// <summary>During a big read, save after this many replays, so quitting halfway doesn't lose the work.</summary>
    const int SaveEvery = 10;

    public Scanner(string replayDir, string dataDir)
    {
        ReplayDir = replayDir;
        DataDir = dataDir;
        Directory.CreateDirectory(DataDir);
        Load();
        if (LoadProblem != null) Console.WriteLine(LoadProblem);
    }

    public static string DefaultReplayDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FortniteGame", "Saved", "Demos");

    /// <summary>
    /// matches.json holds games whose replays Fortnite has already deleted, so it can't be rebuilt from scratch.
    /// If it can't be read, the file is kept under another name (never overwritten) and the backup is used.
    /// </summary>
    void Load()
    {
        if (!File.Exists(CachePath)) return;   // first start (or the user deleted it to start over)
        if (TryLoad(CachePath, out var why) is { } loaded) { Matches = loaded; return; }

        var keptAs = Path.Combine(DataDir, $"matches.damaged-{DateTime.Now:yyyyMMdd-HHmmss}.json");
        try { File.Move(CachePath, keptAs); }
        catch
        {
            try { File.Copy(CachePath, keptAs); }
            catch
            {
                _saveBlocked = true;
                LoadProblem = $"Can't read matches.json ({why}) and can't make a safety copy of it, so nothing new will be saved. Close other programs using it and restart.";
                return;
            }
        }

        var kept = Path.GetFileName(keptAs);
        if (TryLoad(BackupPath, out _) is { } backup)
        {
            Matches = backup;
            LoadProblem = $"matches.json was damaged ({why}). Went back to the backup from one save earlier. The damaged file is kept as {kept}.";
        }
        else
        {
            LoadProblem = $"matches.json was damaged ({why}) and there's no backup. Starting again from the replays Fortnite still has. The damaged file is kept as {kept}.";
        }
    }

    /// <summary>The saved games in a file, or null if it's missing or can't be read.</summary>
    static Dictionary<string, MatchResult>? TryLoad(string path, out string? why)
    {
        why = null;
        if (!File.Exists(path)) return null;
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<Dictionary<string, MatchResult>>(fs) ?? new();
        }
        catch (Exception ex)
        {
            why = ex is JsonException ? "not valid JSON" : ex.Message;
            return null;
        }
    }

    public void Save()
    {
        if (_saveBlocked) throw new IOException("matches.json couldn't be read on start, so it isn't being overwritten.");
        var tmp = CachePath + ".tmp";
        using (var fs = File.Create(tmp)) JsonSerializer.Serialize(fs, Matches, Json);
        if (File.Exists(CachePath))
        {
            // swap in the new file and keep the old one as matches.json.bak, in one step
            try
            {
                File.Replace(tmp, CachePath, BackupPath, ignoreMetadataErrors: true);
                return;
            }
            catch (Exception ex) when (ex is IOException or PlatformNotSupportedException or UnauthorizedAccessException)
            {
                // some drives can't swap files in one step: copy the old one, then move the new one in
                if (File.Exists(CachePath)) File.Copy(CachePath, BackupPath, overwrite: true);
            }
        }
        File.Move(tmp, CachePath, overwrite: true);
    }

    /// <summary>True if this replay still needs reading (new, or changed since we last read it).</summary>
    public bool NeedsRead(FileInfo f) =>
        !Matches.TryGetValue(f.Name, out var m) || m.Size != f.Length || m.LastWrite != f.LastWriteTimeUtc
        || m.ReaderVersion < MatchResult.CurrentReaderVersion;

    /// <summary>
    /// Puts a freshly read result in the cache. A read that failed never replaces an earlier good result: the good
    /// one stays and is marked as checked, so the same file isn't tried again on every pass.
    /// Returns false when nothing was stored because the file couldn't be opened at all (it gets tried again).
    /// </summary>
    public bool Store(MatchResult r)
    {
        if (r.CouldNotOpen) return false;
        if (r.Error != null && Matches.TryGetValue(r.File, out var old) && old.Error == null)
        {
            old.Size = r.Size;
            old.LastWrite = r.LastWrite;
            old.ReaderVersion = r.ReaderVersion;
            old.Warnings.Add($"Reading it again failed ({r.Error}); kept the earlier result.");
            return true;
        }
        Matches[r.File] = r;
        return true;
    }

    /// <summary>
    /// Reads every replay in the folder that isn't cached yet. Returns how many were read.
    /// <paramref name="saved"/> is called after each save along the way (every <see cref="SaveEvery"/> replays),
    /// while nothing else is changing <see cref="Matches"/>.
    /// </summary>
    public int ScanAll(bool skipInProgress, Action<string>? log = null, Action<int, int>? progress = null, Action? saved = null)
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
        var unsaved = 0;
        var gate = new object();
        var sw = Stopwatch.StartNew();
        Parallel.ForEach(todo, new ParallelOptions { MaxDegreeOfParallelism = Workers() }, f =>
        {
            var r = LootReader.Read(f.FullName);
            lock (gate)
            {
                if (Store(r) && ++unsaved >= SaveEvery)
                {
                    Save();
                    unsaved = 0;
                    saved?.Invoke();
                }
            }
            var n = Interlocked.Increment(ref done);
            progress?.Invoke(n, todo.Count);
            if (r.Error != null) log?.Invoke($"  [{n}/{todo.Count}] {f.Name}: could not read ({r.Error})");
            else if (n % 10 == 0 || n == todo.Count) log?.Invoke($"  [{n}/{todo.Count}] {sw.Elapsed.TotalSeconds:F0}s");
        });

        if (unsaved > 0) Save();
        return todo.Count;
    }

    /// <summary>
    /// How many replays to read at once. Reading one takes a few hundred MB for a moment, so while Fortnite is
    /// running it's two at a time; otherwise up to six.
    /// </summary>
    static int Workers() => FortniteRunning() ? 2 : Math.Clamp(Environment.ProcessorCount - 1, 1, 6);

    static bool FortniteRunning()
    {
        try
        {
            var found = Process.GetProcessesByName("FortniteClient-Win64-Shipping");
            foreach (var p in found) p.Dispose();
            return found.Length > 0;
        }
        catch { return false; }
    }

    /// <summary>
    /// A replay is still being written while Fortnite is in that match. We treat it as in progress if it was
    /// modified in the last 5 seconds or Fortnite still has it open; the replay's own "live" flag is checked
    /// after reading as well.
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
