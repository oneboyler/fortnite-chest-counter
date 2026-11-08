using System.Runtime;

namespace SquadChests;

/// <summary>
/// Reads every replay once, then keeps an eye on the replay folder: when Fortnite stops writing a replay
/// (the game ended), it reads that game and adds it to the totals. Runs on its own background thread.
/// </summary>
public sealed class GameWatcher
{
    public const string WatchingStatus = "Watching for new games";
    public const string InGameStatus = "Game in progress, will count it when it ends";
    const string FolderMissingStatus = "Fortnite replay folder not found (turn on replays in Fortnite settings)";

    /// <summary>A replay Fortnite never marked as finished (crash, alt-F4) is counted anyway after this long.</summary>
    static readonly TimeSpan LiveGrace = TimeSpan.FromMinutes(10);

    public Scanner Scanner { get; }

    /// <summary>Copy of all results, safe to read from any thread. Replaced (not changed) on every update.</summary>
    public IReadOnlyList<MatchResult> Snapshot { get; private set; }
    public string Status { get; private set; } = "Starting...";
    /// <summary>(done, total) while the first big read is running, otherwise null.</summary>
    public (int Done, int Total)? ScanProgress { get; private set; }
    /// <summary>When the game being played right now started recording (local time), or null when not in a game.</summary>
    public DateTime? InGameSince { get; private set; }

    /// <summary>Snapshot, Status or ScanProgress changed. Raised on the watcher thread.</summary>
    public event Action? Changed;
    /// <summary>A game that just ended was read (custom games included). Raised on the watcher thread.</summary>
    public event Action<MatchResult>? GameAdded;

    readonly TimeSpan _interval;
    readonly Dictionary<string, (long Size, DateTime Write)> _stillLive = new();
    readonly CancellationTokenSource _cts = new();

    public GameWatcher(string replayDir, string dataDir, TimeSpan? interval = null)
    {
        Scanner = new Scanner(replayDir, dataDir);
        Snapshot = Scanner.Matches.Values.ToList();
        _interval = interval ?? TimeSpan.FromSeconds(5);
    }

    /// <param name="initialScan">Read all replays that aren't cached yet before watching.</param>
    public void Start(bool initialScan = true)
    {
        var t = new Thread(() => Run(initialScan, _cts.Token))
        {
            IsBackground = true,
            Priority = ThreadPriority.BelowNormal,
            Name = "GameWatcher",
        };
        t.Start();
    }

    public void Stop() => _cts.Cancel();

    void Run(bool initialScan, CancellationToken token)
    {
        if (initialScan)
        {
            try
            {
                SetStatus("Reading your replays...");
                var read = Scanner.ScanAll(skipInProgress: true, progress: (done, total) =>
                {
                    ScanProgress = (done, total);
                    Status = $"Reading your replays {done}/{total}...";
                    Changed?.Invoke();
                });
                ScanProgress = null;
                Publish();
                if (read > 0) FreeMemory();
            }
            catch (Exception ex)
            {
                ScanProgress = null;
                SetStatus("Could not read replays: " + ex.Message);
            }
        }
        SetStatus(Directory.Exists(Scanner.ReplayDir) ? WatchingStatus : FolderMissingStatus);

        while (!token.WaitHandle.WaitOne(_interval))
        {
            try { Tick(); }
            catch (Exception ex) { SetStatus("Problem checking replays: " + ex.Message); }
        }
    }

    void Tick()
    {
        if (!Directory.Exists(Scanner.ReplayDir))
        {
            if (Status != FolderMissingStatus) SetStatus(FolderMissingStatus);
            return;
        }

        var inGame = false;
        DateTime? since = null;
        void InGame(FileInfo f)
        {
            inGame = true;
            if (since == null || f.CreationTime > since) since = f.CreationTime;
        }

        var files = new DirectoryInfo(Scanner.ReplayDir).GetFiles("*.replay").Where(Scanner.NeedsRead).OrderBy(f => f.LastWriteTimeUtc).ToList();
        foreach (var f in files)
        {
            f.Refresh();
            if (!f.Exists) continue;
            if (Scanner.LooksInProgress(f)) { InGame(f); continue; }

            // Already read this exact file once and it was still marked live: don't re-read until it changes.
            var age = DateTime.UtcNow - f.LastWriteTimeUtc;
            if (_stillLive.TryGetValue(f.Name, out var seen) && seen == (f.Length, f.LastWriteTimeUtc) && age < LiveGrace)
            {
                InGame(f);
                continue;
            }

            SetStatus("Reading your last game...");
            var r = LootReader.Read(f.FullName);
            if (r.IsLive && age < LiveGrace)
            {
                _stillLive[f.Name] = (f.Length, f.LastWriteTimeUtc);
                InGame(f);
                continue;
            }
            _stillLive.Remove(f.Name);
            r.IsLive = false; // finished, or Fortnite closed without finalizing it

            Scanner.Matches[r.File] = r;
            Scanner.Save();
            Publish();
            if (r.Error == null && r.OwnerId != null) GameAdded?.Invoke(r);
            FreeMemory();
        }

        var status = inGame ? InGameStatus : WatchingStatus;
        if (status != Status || since != InGameSince)
        {
            InGameSince = since;
            SetStatus(status);
        }
    }

    void SetStatus(string s)
    {
        Status = s;
        Changed?.Invoke();
    }

    void Publish()
    {
        Snapshot = Scanner.Matches.Values.ToList();
        Changed?.Invoke();
    }

    /// <summary>Reading a replay needs a lot of memory for a moment; give it back so the app stays small in the background.</summary>
    static void FreeMemory()
    {
        GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
    }
}
