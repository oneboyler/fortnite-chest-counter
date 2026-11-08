using System.Diagnostics;

namespace ChestCounter;

static class Program
{
    const string RunningMutex = @"Local\ChestCounter.Running";
    const string ShowEvent = @"Local\ChestCounter.Show";

    [STAThread]
    static void Main(string[] args)
    {
        // Build helper: writes the exe icon.
        if (args.Length == 2 && args[0] == "--make-icon")
        {
            ChestArt.WriteIco(args[1]);
            return;
        }

        // Build helper: saves a picture of the setup window.
        if (args.Length >= 2 && args[0] == "--snapshot-setup")
        {
            ApplicationConfiguration.Initialize();
            using var setup = new SetupForm(args.Length >= 3 ? args[2] : SquadChests.Scanner.DefaultReplayDir(), true);
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
            var flash = args.Contains("--flash") ? new Flash("+67", OverlayForm.Green, now.AddSeconds(20)) : null;
            using var form = new OverlayForm();
            form.SetView(OverlayModel.Build(sum, inGame != null ? SquadChests.GameWatcher.InGameStatus : SquadChests.GameWatcher.WatchingStatus,
                null, inGame, null, flash, args.Contains("--hint") ? "F8 hide/show   Shift+F8 move   Ctrl+F8 page\ncounts update right after each game" : null, OverlayForm.Dim, now,
                args.Contains("--graph") ? Settings.PageGraph : args.Contains("--compact") ? Settings.PageCompact : Settings.PageFull));
            using var bmp = form.Snapshot();
            bmp.Save(args[1], System.Drawing.Imaging.ImageFormat.Png);
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

        // Stay out of the game's way: reading replays only uses spare CPU.
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => LogError(dataDir, e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => LogError(dataDir, e.ExceptionObject as Exception);

        ApplicationConfiguration.Initialize();
        var app = new TrayApp();
        ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => app.ShowFromOtherInstance(), null, Timeout.Infinite, executeOnlyOnce: false);
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
