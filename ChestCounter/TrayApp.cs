using System.Diagnostics;
using SquadChests;

namespace ChestCounter;

/// <summary>Runs in the tray: owns the replay watcher, the overlay, the tray icon/menu and the F8 keys.</summary>
sealed class TrayApp : ApplicationContext
{
    readonly string _dataDir;
    readonly Settings _settings;
    readonly string[] _treasure;
    readonly GameWatcher _watcher;
    readonly OverlayForm _overlay;
    readonly NotifyIcon _tray;
    readonly ContextMenuStrip _menu;
    readonly ToolStripMenuItem _miShow, _miMove, _miCompact, _miGraph, _miMonth, _miNotify, _miStartup, _miUpdate, _miAutoUpdate, _miVersion;

    /// <summary>"v1.7" (or "v1.7.1"): the same form as the release tags.</summary>
    static string VersionText => "v" + (Updater.Current.Build > 0 ? Updater.Current.ToString(3) : Updater.Current.ToString(2));
    readonly ToolStripSeparator _updateSep = new() { Visible = false };
    readonly System.Windows.Forms.Timer _keys, _clock, _updateTimer;
    Updater.Release? _update;
    DateTime _updateFoundAt;
    bool _updating;
    readonly DateTime _startedAt = DateTime.Now;

    IReadOnlyList<MatchResult>? _summarized;
    Stats.Summary _sum = new();
    Flash? _flash;
    int _refreshQueued;
    HotKey[] _hotkeys;
    readonly HashSet<Keys> _keysDown = new();
    int _ticks;

    public TrayApp()
    {
        _dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        Directory.CreateDirectory(_dataDir);
        _settings = Settings.Load(_dataDir);
        _hotkeys = HotKey.Load(_settings);
        _treasure = Stats.LoadTreasurePatterns(_dataDir);
        _watcher = new GameWatcher(Scanner.DefaultReplayDir(), _dataDir);

        // ---- menu (tray icon right-click, and overlay right-click in move mode) ----
        _miShow = new ToolStripMenuItem("Hide overlay", null, (_, _) => ToggleVisible());
        _miMove = new ToolStripMenuItem("Move overlay", null, (_, _) => ToggleMove());
        // pages: full / compact / graph (the switch-view key cycles through them)
        _miCompact = new ToolStripMenuItem("Compact view", null, (_, _) => SetPage(_settings.Page == Settings.PageCompact ? Settings.PageFull : Settings.PageCompact));
        _miGraph = new ToolStripMenuItem("Show graph", null, (_, _) => SetPage(_settings.Page == Settings.PageGraph ? Settings.PageFull : Settings.PageGraph));
        _miMonth = new ToolStripMenuItem("Month view (chests per day)", null, (_, _) => SetPage(_settings.Page == Settings.PageMonth ? Settings.PageFull : Settings.PageMonth));
        // which version this is, at the top of the menu; "(up to date)" is added once GitHub has confirmed it
        _miVersion = new ToolStripMenuItem($"Chest Counter {VersionText}") { Enabled = false };
        _miNotify = new ToolStripMenuItem("Windows pop-up after each game", null, (_, _) =>
        {
            _settings.NotifyAfterGame = !_settings.NotifyAfterGame;
            _settings.Save(_dataDir);
        });
        _miStartup = new ToolStripMenuItem("Start with Windows", null, (_, _) =>
        {
            try { StartWithWindows.Set(!StartWithWindows.IsOn()); }
            catch (Exception ex) { MessageBox.Show("Couldn't change that: " + ex.Message, "Chest Counter"); }
        });
        _miUpdate = new ToolStripMenuItem("Update", null, (_, _) => InstallUpdate()) { Visible = false };
        _miUpdate.Font = new Font(_miUpdate.Font, FontStyle.Bold);
        _miAutoUpdate = new ToolStripMenuItem("Check for updates", null, (_, _) =>
        {
            _settings.CheckForUpdates = !_settings.CheckForUpdates;
            _settings.Save(_dataDir);
            if (_settings.CheckForUpdates) _ = CheckForUpdate();
        });
        _menu = new ContextMenuStrip();
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _miVersion, _miUpdate, new ToolStripSeparator(),
            _miShow, _miMove, _miCompact, _miGraph, _miMonth,
            new ToolStripMenuItem("New session", null, (_, _) => NewSession()),
            new ToolStripSeparator(),
            _miNotify, _miStartup, _miAutoUpdate,
            new ToolStripSeparator(),
            new ToolStripMenuItem("Full report (per mode, teammates...)", null, (_, _) => OpenReport()),
            new ToolStripMenuItem("Change keys", null, (_, _) => ShowSetup()),
            new ToolStripMenuItem("Setup and help", null, (_, _) => ShowSetup()),
            new ToolStripMenuItem("Open replay folder", null, (_, _) => OpenFolder(_watcher.Scanner.ReplayDir)),
            new ToolStripMenuItem("Open data folder", null, (_, _) => OpenFolder(_dataDir)),
            new ToolStripSeparator(),
            new ToolStripMenuItem("Exit", null, (_, _) => Exit()),
        });
        _menu.Opening += (_, _) =>
        {
            _miShow.Text = _overlay!.Visible ? "Hide overlay" : "Show overlay";
            _miMove.Checked = _overlay.Moving;
            _miCompact.Checked = _settings.Page == Settings.PageCompact;
            _miGraph.Checked = _settings.Page == Settings.PageGraph;
            _miMonth.Checked = _settings.Page == Settings.PageMonth;
            _miNotify.Checked = _settings.NotifyAfterGame;
            _miStartup.Checked = StartWithWindows.IsOn();
            _miAutoUpdate.Checked = _settings.CheckForUpdates;
            _miShow.ShortcutKeyDisplayString = _hotkeys[HotKey.Hide].ToString();
            _miMove.ShortcutKeyDisplayString = _hotkeys[HotKey.Move].ToString();
            _miCompact.ShortcutKeyDisplayString = _miGraph.ShortcutKeyDisplayString = _miMonth.ShortcutKeyDisplayString = _hotkeys[HotKey.Page].ToString();
        };

        // ---- overlay ----
        _overlay = new OverlayForm { MoveMenu = _menu };
        _overlay.MoveEnded += () =>
        {
            _pageCenter = null;
            _settings.Left = _overlay.Left;
            _settings.Top = _overlay.Top;
            _settings.Save(_dataDir);
        };
        _ = _overlay.Handle; // create it now so background updates work while it's hidden

        // ---- tray icon ----
        _tray = new NotifyIcon
        {
            Icon = ChestArt.Icon(SystemInformation.SmallIconSize.Width),
            Text = "Chest Counter",
            ContextMenuStrip = _menu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ToggleVisible(); };
        // the saved games couldn't be read: say what was done about it (the damaged file is kept, never overwritten)
        if (_watcher.Scanner.LoadProblem is string problem)
            _tray.ShowBalloonTip(15000, "Chest Counter: saved games", problem.Length > 250 ? problem[..250] + "…" : problem, ToolTipIcon.Warning);

        _watcher.Changed += QueueRefresh;
        _watcher.GameAdded += m => OnUi(() => OnGameAdded(m));

        // F8 = hide/show, Shift+F8 = move, Ctrl+F8 = switch view (changeable). Only reads whether the key is held, so the game still gets the key too.
        _keys = new System.Windows.Forms.Timer { Interval = 30 };
        _keys.Tick += (_, _) => PollKeys();
        _keys.Start();

        // session clock, flash timeout, and keeping the overlay above the game
        _clock = new System.Windows.Forms.Timer { Interval = 1000 };
        _clock.Tick += (_, _) =>
        {
            RefreshView();
            if (++_ticks % 2 == 0 && !_overlay.Moving) _overlay.BringToTop();
        };
        _clock.Start();

        // update check: shortly after start, then every 6 hours
        _updateTimer = new System.Windows.Forms.Timer { Interval = 15_000 };
        _updateTimer.Tick += (_, _) => { _updateTimer.Interval = 6 * 3600 * 1000; _ = CheckForUpdate(); };
        _updateTimer.Start();

        RefreshView();
        PlaceOverlay();
        if (_settings.OverlayVisible) _overlay.Show();
        _watcher.Start();
        if (!_settings.SetupDone) _overlay.BeginInvoke(ShowSetup);
    }

    async Task CheckForUpdate()
    {
        if (!_settings.CheckForUpdates || _update != null) return;
        Updater.Release? r;
        try { r = await Updater.CheckAsync(); }
        catch { return; }   // offline, GitHub down...: try again later
        if (r == null) return;
        if (!Updater.IsNewer(r))
        {
            _miVersion.Text = $"Chest Counter {VersionText} (up to date)";
            return;
        }
        _miVersion.Text = $"Chest Counter {VersionText} (update available)";
        _update = r;
        _updateFoundAt = DateTime.Now;
        _miUpdate.Text = $"Update to {r.Tag} (new!)";
        _miUpdate.Visible = _updateSep.Visible = true;
        _tray.ShowBalloonTip(8000, $"Chest Counter {r.Tag} is out",
            "Right-click the chest icon in the tray and click \"Update\". Your counted games are kept.", ToolTipIcon.Info);
        RefreshView();
    }

    async void InstallUpdate()
    {
        if (_update == null || _updating) return;
        _updating = true;
        _miUpdate.Text = "Downloading update...";
        _miUpdate.Enabled = false;
        try
        {
            var src = await Updater.DownloadAsync(_update);
            Updater.InstallAndRestart(src);
            Exit();
        }
        catch (Exception ex)
        {
            _updating = false;
            _miUpdate.Text = $"Update to {_update.Tag} (new!)";
            _miUpdate.Enabled = true;
            if (MessageBox.Show($"The update didn't work: {ex.Message}\n\nOpen the download page instead?", "Chest Counter",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning) == DialogResult.Yes)
                Process.Start(new ProcessStartInfo(Updater.ReleasesPage) { UseShellExecute = true });
        }
    }

    SetupForm? _setup;

    /// <summary>First start (and tray menu): checks Fortnite's settings and explains the keys.</summary>
    void ShowSetup()
    {
        if (_setup is { IsDisposed: false }) { _setup.Activate(); return; }
        // first start: suggest starting with Windows; later: show what it's set to now
        _setup = new SetupForm(_watcher.Scanner.ReplayDir, _settings.SetupDone ? StartWithWindows.IsOn() : true, _hotkeys);
        _setup.KeysChanged += keys =>
        {
            _hotkeys = keys;
            HotKey.Store(_settings, keys);
            _settings.Save(_dataDir);
            // the key just pressed to pick it is still held: don't let that count as a press
            foreach (var k in keys) if (Native.IsDown((int)k.Key)) _keysDown.Add(k.Key);
            RefreshView();
        };
        _setup.FormClosed += (_, _) =>
        {
            try { if (_setup!.StartWithWindowsChecked != StartWithWindows.IsOn()) StartWithWindows.Set(_setup.StartWithWindowsChecked); }
            catch (Exception ex) { MessageBox.Show("Couldn't change \"Start with Windows\": " + ex.Message, "Chest Counter"); }
            _settings.SetupDone = true;
            _settings.Save(_dataDir);
        };
        _setup.Show();
        _setup.Activate();
    }

    /// <summary>Another copy of the app was started: show the overlay instead.</summary>
    public void ShowFromOtherInstance() => OnUi(() => { if (!_overlay.Visible) ToggleVisible(); });

    /// <summary>The installer or uninstaller asked this copy to close ("Chest Counter.exe" --exit).</summary>
    public void ExitFromOtherInstance() => OnUi(Exit);

    void OnUi(Action a)
    {
        try { if (!_overlay.IsDisposed) _overlay.BeginInvoke(a); }
        catch (InvalidOperationException) { /* shutting down */ }
    }

    void QueueRefresh()
    {
        // The watcher can fire many times a second while reading; redraw at most once per UI turn.
        if (Interlocked.Exchange(ref _refreshQueued, 1) == 1) return;
        OnUi(() => { _refreshQueued = 0; RefreshView(); });
    }

    void RefreshView()
    {
        var snap = _watcher.Snapshot;
        if (!ReferenceEquals(snap, _summarized))
        {
            _summarized = snap;
            _sum = Stats.Summarize(snap, _treasure);
        }

        string? hint = null;
        var hintColor = OverlayForm.Dim;
        if (_overlay.Moving)
        {
            hint = $"drag to move  -  right-click for menu\n{_hotkeys[HotKey.Move]} again to lock";
            hintColor = OverlayForm.Gold;
        }
        else if (_update != null && !_updating && DateTime.Now - _updateFoundAt < TimeSpan.FromMinutes(2))
        {
            hint = $"Update {_update.Tag} is out: right-click\nthe chest icon in the tray > Update";
            hintColor = OverlayForm.Gold;
        }
        else if (DateTime.Now - _startedAt < TimeSpan.FromSeconds(5))
        {
            hint = HotKey.Hint(_hotkeys);
        }

        _overlay.SetView(OverlayModel.Build(_sum, _watcher.Status, _watcher.ScanProgress, _watcher.InGameSince,
            _settings.SessionStart, _flash, hint, hintColor, DateTime.Now, _settings.Page));
        var tip = _sum.Games > 0 ? $"Chest Counter {VersionText}: {_sum.Chests:N0} chests, {_sum.PerGame:F1}/game" : $"Chest Counter {VersionText}";
        _tray.Text = tip.Length > 63 ? tip[..63] : tip;
    }

    void OnGameAdded(MatchResult m)
    {
        if (Stats.SkipReason(m, Stats.CurrentSeason(_watcher.Snapshot.Append(m))) is string why)
        {
            _flash = new Flash($"{why}, not counted", OverlayForm.Dim, DateTime.Now.AddSeconds(20), Counted: false, ShowText: true);
            RefreshView();
            return;
        }
        var c = Stats.Count(m, _treasure);
        var n = c.MeOpened + c.MeDestroyed;
        var treasure = c.TreasureChests.SelectMany(x => x.Items).Concat(c.TreasureInLobby)
            .Where(i => Stats.IsTreasure(i, _treasure)).Distinct().Select(Stats.Pretty).ToList();
        _flash = treasure.Count > 0
            ? new Flash($"+{n} TREASURE!", OverlayForm.Gold, DateTime.Now.AddMinutes(5), ShowText: true)
            : new Flash($"+{n}", OverlayForm.Green, DateTime.Now.AddSeconds(20));
        RefreshView();

        if (!_settings.NotifyAfterGame && treasure.Count == 0) return;
        var lines = new List<string>();
        if (treasure.Count > 0) lines.Add(string.Join(", ", treasure));
        lines.Add($"+{n} chests" + (m.OwnTreasures > 0 ? $", +{m.OwnTreasures} season treasure" : "")
                  + (m.SharedTreasures > 0 ? $" (+{m.SharedTreasures} from teammates)" : ""));
        lines.Add($"Total {_sum.Chests:N0} chests · {_sum.PerGame:F1} per game");
        var fresh = Stats.NewItems(m, _watcher.Snapshot);
        if (fresh.Count > 0) lines.Add("Never seen before: " + string.Join(", ", fresh.Select(Stats.Pretty)));
        var title = treasure.Count > 0 ? "TREASURE!" : $"{Stats.ModeOf(m)}: +{n} chests";
        var text = string.Join("\n", lines);
        _tray.ShowBalloonTip(6000, title, text.Length > 250 ? text[..250] + "…" : text, ToolTipIcon.None);
    }

    void PollKeys()
    {
        bool ctrl = Native.IsDown(Native.VK_CONTROL), shift = Native.IsDown(Native.VK_SHIFT), alt = Native.IsDown(Native.VK_MENU);
        int best = -1;
        foreach (var key in _hotkeys.Select(h => h.Key).Distinct())
        {
            var down = Native.IsDown((int)key);
            var pressed = down && !_keysDown.Contains(key);
            if (down) _keysDown.Add(key); else _keysDown.Remove(key);
            if (!pressed) continue;
            // e.g. Shift+F8 matches both "F8" and "Shift+F8": the one with more held modifiers wins
            for (var i = 0; i < _hotkeys.Length; i++)
                if (_hotkeys[i].Key == key && _hotkeys[i].ModsHeld(ctrl, shift, alt)
                    && (best < 0 || _hotkeys[i].ModCount > _hotkeys[best].ModCount))
                    best = i;
        }
        // while picking a new key in the setup window, keys only pick
        if (best < 0 || _setup is { IsDisposed: false, Picking: true }) return;
        switch (best)
        {
            case HotKey.Hide: ToggleVisible(); break;
            case HotKey.Move: ToggleMove(); break;
            case HotKey.Page: NextPage(); break;
        }
    }

    void ToggleVisible()
    {
        if (_overlay.Visible)
        {
            _overlay.SetMoving(false);
            _overlay.Hide();
        }
        else
        {
            RefreshView();
            _overlay.Show();
            _overlay.BringToTop();
        }
        _settings.OverlayVisible = _overlay.Visible;
        _settings.Save(_dataDir);
    }

    void ToggleMove()
    {
        if (!_overlay.Visible) ToggleVisible();
        _overlay.SetMoving(!_overlay.Moving);
        RefreshView();
    }

    // the switch-view key goes full -> graph -> month -> compact -> full
    static readonly int[] PageOrder = [Settings.PageFull, Settings.PageGraph, Settings.PageMonth, Settings.PageCompact];
    void NextPage() => SetPage(PageOrder[(Array.IndexOf(PageOrder, _settings.Page) + 1) % PageOrder.Length]);

    // Where the middle of the overlay is, so switching views keeps it centered on the same spot
    // (kept as exact numbers so going round the views many times doesn't creep). Reset when it's moved.
    PointF? _pageCenter;

    void SetPage(int page)
    {
        if (!_overlay.Visible) ToggleVisible();
        var b = _overlay.Bounds;
        _pageCenter ??= new PointF(b.Left + b.Width / 2f, b.Top + b.Height / 2f);
        _settings.Page = Math.Clamp(page, 0, Settings.PageCount - 1);
        RefreshView();

        var c = _pageCenter.Value;
        var size = _overlay.Size;
        var screen = Screen.FromPoint(Point.Round(c)).WorkingArea;
        var x = (int)Math.Round(c.X - size.Width / 2f);
        var y = (int)Math.Round(c.Y - size.Height / 2f);
        x = Math.Max(screen.Left, Math.Min(x, screen.Right - size.Width));
        y = Math.Max(screen.Top, Math.Min(y, screen.Bottom - size.Height));
        _overlay.Location = new Point(x, y);
        _settings.Left = x;
        _settings.Top = y;
        _settings.Save(_dataDir);
    }

    void NewSession()
    {
        _settings.SessionStart = DateTime.Now;
        _settings.Save(_dataDir);
        RefreshView();
    }

    void PlaceOverlay()
    {
        var k = _overlay.DeviceDpi / 96f;
        var size = _overlay.Size;
        if (_settings.Left is int l && _settings.Top is int t
            && Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(new Rectangle(l, t, size.Width, 40))))
        {
            _overlay.Location = new Point(l, t);
            return;
        }
        // Below where the Python counter sits by default, so both fit.
        var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
        _overlay.Location = new Point(b.Left + (int)(24 * k), b.Top + (int)(520 * k));
    }

    void OpenReport()
    {
        var bat = Path.Combine(AppContext.BaseDirectory, "Chest Stats.bat");
        if (File.Exists(bat)) Process.Start(new ProcessStartInfo(bat) { UseShellExecute = true });
        else MessageBox.Show($"Not found:\n{bat}", "Chest Counter");
    }

    static void OpenFolder(string path)
    {
        if (Directory.Exists(path)) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        else MessageBox.Show($"Folder not found:\n{path}", "Chest Counter");
    }

    void Exit()
    {
        if (_overlay.Moving) _overlay.SetMoving(false);
        _settings.Save(_dataDir);
        _watcher.Stop();
        _keys.Stop();
        _clock.Stop();
        _updateTimer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _overlay.Close();
        ExitThread();
    }
}
