using System.Diagnostics;

namespace ChestCounter;

static class Program
{
    const string RunningMutex = @"Local\ChestCounter.Running";
    const string ShowEvent = @"Local\ChestCounter.Show";

    // One pair per program folder, so the installer/uninstaller can close exactly the copy it owns
    // and leave a portable copy running from somewhere else alone.
    static readonly string FolderId = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(AppContext.BaseDirectory.TrimEnd('\\', '/').ToUpperInvariant())))[..16];
    static readonly string FolderMutex = @"Local\ChestCounter.Folder." + FolderId;
    static readonly string ExitEvent = @"Local\ChestCounter.Exit." + FolderId;

    [STAThread]
    static void Main(string[] args)
    {
        // Build helper: writes the exe icon.
        if (args.Length == 2 && args[0] == "--make-icon")
        {
            ChestArt.WriteIco(args[1]);
            return;
        }

        // Build helper: saves a picture of the setup window. Optional: --keys=F9,Shift+F9,XButton1 to show other keys.
        if (args.Length >= 2 && args[0] == "--snapshot-setup")
        {
            ApplicationConfiguration.Initialize();
            var keyArg = args.FirstOrDefault(a => a.StartsWith("--keys="))?["--keys=".Length..].Split(',');
            var keys = keyArg?.Select((s, i) => HotKey.Parse(s, HotKey.Defaults[i])).ToArray();
            var dir = args.Length >= 3 && !args[2].StartsWith("--") ? args[2] : SquadChests.Scanner.DefaultReplayDir();
            using var setup = new SetupForm(dir, true, keys);
            using var pic = setup.Snapshot();
            pic.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
            return;
        }

        // Build helper: saves a picture of the stats window from the saved results.
        if (args.Length >= 2 && args[0] == "--snapshot")
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "data");
            if (args.Length >= 3) dir = args[2];
            var scanner = new SquadChests.Scanner(SquadChests.Scanner.DefaultReplayDir(), dir);
            var sum = SquadChests.Stats.Summarize(scanner.Matches.Values, SquadChests.Stats.LoadTreasurePatterns(dir));
            // Optional 4th arg: pretend it's this time (e.g. "2026-11-08 05:50") to see a session in progress.
            var now = args.Length >= 4 && !args[3].StartsWith("--") ? DateTime.Parse(args[3], System.Globalization.CultureInfo.InvariantCulture) : DateTime.Now;
            var inGame = args.Contains("--ingame") ? now.AddMinutes(-7) : (DateTime?)null;
            var flash = args.Contains("--flash") ? new Flash("+67", OverlayForm.Green, now.AddSeconds(20))
                : args.Contains("--flash-treasure") ? new Flash("+67 TREASURE!", OverlayForm.Gold, now.AddMinutes(5), ShowText: true)
                : args.Contains("--flash-skipped") ? new Flash("custom game, not counted", OverlayForm.Dim, now.AddSeconds(20), Counted: false, ShowText: true)
                : null;
            using var form = new OverlayForm();
            form.SetView(OverlayModel.Build(sum, inGame != null ? SquadChests.GameWatcher.InGameStatus : SquadChests.GameWatcher.WatchingStatus,
                null, inGame, null, flash, args.Contains("--hint") ? HotKey.Hint(HotKey.Defaults) : null, OverlayForm.Dim, now,
                args.Contains("--graph") ? Settings.PageGraph : args.Contains("--month") ? Settings.PageMonth
                : args.Contains("--compact") ? Settings.PageCompact : Settings.PageFull));
            using var bmp = form.Snapshot();
            bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
            return;
        }

        // Test helper: install the latest GitHub release over this copy right away (same as clicking "Update").
        if (args.Length == 1 && args[0] == "--update-now")
        {
            var r = Updater.CheckAsync().GetAwaiter().GetResult() ?? throw new InvalidOperationException("no release found");
            Updater.InstallAndRestart(Updater.DownloadAsync(r).GetAwaiter().GetResult());
            return;
        }

        // Used by the installer and uninstaller: asks the copy running from this folder to close (the same as
        // tray icon > Exit) and waits until it has, so its files can be replaced or removed.
        if (args.Length == 1 && args[0] == "--exit")
        {
            try
            {
                using (var ev = EventWaitHandle.OpenExisting(ExitEvent)) ev.Set();
                for (var i = 0; i < 100 && Mutex.TryOpenExisting(FolderMutex, out var m); i++)
                {
                    m.Dispose();
                    Thread.Sleep(100);
                }
                Thread.Sleep(300);   // the process needs a moment after its last line to let go of the exe
            }
            catch { /* no copy is running from this folder */ }
            return;
        }

        using var running = new Mutex(true, RunningMutex, out var firstCopy);
        if (!firstCopy)
        {
            // Already running in the tray: ask that copy to show its window, then quit.
            try { using var ev = EventWaitHandle.OpenExisting(ShowEvent); ev.Set(); } catch { }
            return;
        }
        using var showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEvent);
        using var folder = new Mutex(false, FolderMutex);
        using var exitEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ExitEvent);

        // Stay out of the game's way: reading replays only uses spare CPU.
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogError(dataDir, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogError(dataDir, e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        Installed.SyncVersion();
        var app = new TrayApp();
        ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => app.ShowFromOtherInstance(), null, Timeout.Infinite, executeOnlyOnce: false);
        ThreadPool.RegisterWaitForSingleObject(exitEvent, (_, _) => app.ExitFromOtherInstance(), null, Timeout.Infinite, executeOnlyOnce: true);
        Application.Run(app);
    }

    static void LogError(string dataDir, Exception? ex)
    {
        try
        {
            Directory.CreateDirectory(dataDir);
            File.AppendAllText(Path.Combine(dataDir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}\n\n");
        }
        catch { }
    }
}
