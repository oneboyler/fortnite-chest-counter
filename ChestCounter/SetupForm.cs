namespace ChestCounter;

/// <summary>What the setup window can find out on its own.</summary>
static class SetupChecks
{
    public enum State { Ok, Warn, Bad }
    public record Check(State State, string Title, string Detail);

    public static Check Replays(string replayDir)
    {
        var files = Directory.Exists(replayDir) ? new DirectoryInfo(replayDir).GetFiles("*.replay") : [];
        if (files.Length == 0)
            return new(State.Bad, "No Fortnite replays found",
                "In Fortnite: Settings > Game > Replays, turn on \"Record Replays\" for Battle Royale. " +
                "Then play a game; it gets counted a few seconds after the game ends.");
        var newest = files.Max(f => f.LastWriteTime);
        return new(State.Ok, $"{files.Length} Fortnite replays found (newest {newest:MMM d, HH:mm})",
            "Keep \"Record Replays\" on in Fortnite (Settings > Game > Replays), or new games can't be counted.");
    }

    /// <summary>Fortnite's window mode, from its own settings file: 0 fullscreen, 1 windowed fullscreen, 2 windowed.</summary>
    public static Check WindowMode()
    {
        var ini = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "FortniteGame", "Saved", "Config", "WindowsClient", "GameUserSettings.ini");
        int? mode = null;
        try
        {
            foreach (var line in File.ReadLines(ini))
            {
                foreach (var key in new[] { "LastConfirmedFullscreenMode=", "PreferredFullscreenMode=" })
                    if (line.StartsWith(key) && int.TryParse(line[key.Length..].Trim(), out var m)) mode ??= m;
            }
        }
        catch { /* no Fortnite settings yet */ }

        const string fix = "In Fortnite: Settings > Video > Window Mode, pick \"Windowed Fullscreen\" (looks the same as fullscreen).";
        return mode switch
        {
            0 => new(State.Bad, "Fortnite is set to Fullscreen", "The overlay can't show on top of real Fullscreen. " + fix),
            1 => new(State.Ok, "Fortnite is set to Windowed Fullscreen", "The overlay shows on top of the game."),
            2 => new(State.Ok, "Fortnite is set to Windowed", "The overlay shows on top of the game."),
            _ => new(State.Warn, "Couldn't read Fortnite's window mode", fix),
        };
    }
}

/// <summary>First-start window: checks Fortnite's settings and explains the keys. Reopen from the tray menu.</summary>
sealed class SetupForm : Form
{
    readonly string _replayDir;
    readonly FlowLayoutPanel _checks;
    public CheckBox StartWithWindowsBox { get; }

    public SetupForm(string replayDir, bool startWithWindows)
    {
        _replayDir = replayDir;
        Text = "Chest Counter - setup";
        Icon = ChestArt.Icon(32);
        BackColor = OverlayForm.Bg;
        ForeColor = OverlayForm.Fg;
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18, 14, 18, 14);
        TopMost = true;

        const int width = 520;
        var root = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty, Location = new Point(18, 14) };
        Controls.Add(root);

        Label Para(string s, Font? f = null, Color? c = null, int top = 0) => new()
        {
            Text = s, AutoSize = true, MaximumSize = new Size(width, 0), Font = f ?? Font, ForeColor = c ?? OverlayForm.Fg,
            Margin = new Padding(0, top, 0, 0),
        };
        Font Bold(float size) => new("Segoe UI", size, FontStyle.Bold);

        root.Controls.Add(Para("Chest Counter", Bold(16f), OverlayForm.Gold));
        root.Controls.Add(Para("Counts the chests you open or break and the season treasures you get, " +
                               "straight from your Fortnite replays. It runs quietly in the tray (the chest icon by the clock).",
            c: OverlayForm.Dim));

        root.Controls.Add(Para("CHECKS", Bold(9f), OverlayForm.Dim, 14));
        _checks = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = Padding.Empty };
        root.Controls.Add(_checks);
        FillChecks();

        root.Controls.Add(Para("KEYS (work while you play)", Bold(9f), OverlayForm.Dim, 14));
        foreach (var (key, what) in new[]
                 {
                     ("F8", "hide / show the overlay"),
                     ("Shift+F8", "move it: drag it with the mouse, then Shift+F8 again to lock it"),
                     ("Ctrl+F8", "switch between the numbers and a graph of your games"),
                 })
        {
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 4, 0, 0) };
            row.Controls.Add(new Label { Text = key, AutoSize = false, Width = 82, Font = Bold(10f), ForeColor = OverlayForm.Gold, Margin = Padding.Empty });
            row.Controls.Add(new Label { Text = what, AutoSize = true, MaximumSize = new Size(width - 90, 0), Margin = Padding.Empty });
            root.Controls.Add(row);
        }
        root.Controls.Add(Para("Right-click the tray icon for the menu: full report, graph, start with Windows, exit.",
            c: OverlayForm.Dim, top: 6));

        root.Controls.Add(Para("GOOD TO KNOW", Bold(9f), OverlayForm.Dim, 14));
        root.Controls.Add(Para("• The first start reads your last ~100 games. That takes about a minute; the overlay shows the progress.", top: 4));
        root.Controls.Add(Para("• After that, each game is counted a few seconds after it ends. Fortnite locks the replay while you play.", top: 2));
        root.Controls.Add(Para("• Custom games (fewer than 10 players) are left out.", top: 2));

        StartWithWindowsBox = new CheckBox
        {
            Text = "Start with Windows (recommended, so no game gets missed)", Checked = startWithWindows, AutoSize = true,
            ForeColor = OverlayForm.Fg, Margin = new Padding(0, 14, 0, 0),
        };
        root.Controls.Add(StartWithWindowsBox);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 14, 0, 0) };
        Button Btn(string s, bool primary) => new()
        {
            Text = s, AutoSize = true, Padding = new Padding(10, 3, 10, 3), FlatStyle = FlatStyle.Flat,
            BackColor = primary ? OverlayForm.Gold : ColorTranslator.FromHtml("#1f252c"),
            ForeColor = primary ? OverlayForm.Bg : OverlayForm.Fg, Font = Bold(10f), Margin = new Padding(0, 0, 10, 0),
        };
        var done = Btn("Done", true);
        done.FlatAppearance.BorderSize = 0;
        done.Click += (_, _) => Close();
        var again = Btn("Check again", false);
        again.FlatAppearance.BorderColor = ColorTranslator.FromHtml("#3d4856");
        again.Click += (_, _) => FillChecks();
        buttons.Controls.Add(done);
        buttons.Controls.Add(again);
        root.Controls.Add(buttons);
        AcceptButton = done;
    }

    void FillChecks()
    {
        _checks.SuspendLayout();
        _checks.Controls.Clear();
        foreach (var c in new[] { SetupChecks.Replays(_replayDir), SetupChecks.WindowMode() })
        {
            var (mark, color) = c.State switch
            {
                SetupChecks.State.Ok => ("✓", OverlayForm.Green),
                SetupChecks.State.Warn => ("!", OverlayForm.Gold),
                _ => ("✗", OverlayForm.Red),
            };
            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 6, 0, 0) };
            row.Controls.Add(new Label { Text = mark, AutoSize = false, Width = 24, Font = new Font("Segoe UI Symbol", 12f, FontStyle.Bold), ForeColor = color, Margin = Padding.Empty });
            var text = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = Padding.Empty };
            text.Controls.Add(new Label { Text = c.Title, AutoSize = true, MaximumSize = new Size(496, 0), Font = new Font("Segoe UI", 10f, FontStyle.Bold), ForeColor = color, Margin = Padding.Empty });
            text.Controls.Add(new Label { Text = c.Detail, AutoSize = true, MaximumSize = new Size(496, 0), ForeColor = OverlayForm.Dim, Margin = Padding.Empty });
            row.Controls.Add(text);
            _checks.Controls.Add(row);
        }
        _checks.ResumeLayout();
    }

    /// <summary>Build helper: a picture of this window without showing it.</summary>
    public Bitmap Snapshot()
    {
        StartPosition = FormStartPosition.Manual;
        Location = new Point(-5000, -5000);
        ShowInTaskbar = false;
        Show();
        Application.DoEvents();
        var bmp = new Bitmap(Width, Height);
        DrawToBitmap(bmp, new Rectangle(Point.Empty, Size));
        return bmp;
    }
}
