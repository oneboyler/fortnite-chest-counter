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

/// <summary>
/// First-start window: checks Fortnite's settings and explains the keys. Reopen from the tray menu.
/// Drawn by hand in the same glass style as the overlay (rounded, see-through), so it has its own
/// little buttons and checkbox instead of standard Windows controls.
/// </summary>
sealed class SetupForm : Form
{
    readonly string _replayDir;
    SetupChecks.Check[] _checks = [];
    public bool StartWithWindowsChecked { get; private set; }

    // the keys; click one, then press the new key (or a mouse side button)
    readonly HotKey[] _keys;
    int _picking = -1;
    public bool Picking => _picking >= 0;
    public event Action<HotKey[]>? KeysChanged;

    // clickable areas from the last layout, and what the mouse is over / pressed on
    readonly Dictionary<string, Rectangle> _hits = new();
    string? _hover, _pressed;

    Font _title = null!, _sub = null!, _section = null!, _bold = null!, _text = null!, _key = null!, _btn = null!, _symbol = null!;
    float _fontsFor;

    float K => DeviceDpi / 96f;
    int Px(float v) => (int)Math.Round(v * K);

    public SetupForm(string replayDir, bool startWithWindows, HotKey[]? keys = null)
    {
        _replayDir = replayDir;
        _keys = (HotKey[])(keys ?? HotKey.Defaults).Clone();
        StartWithWindowsChecked = startWithWindows;
        Text = "Chest Counter - setup";
        Icon = ChestArt.Icon(32);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        _checks = [SetupChecks.Replays(_replayDir), SetupChecks.WindowMode()];

        Size = Layout(null);
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(area.Left + (area.Width - Width) / 2, area.Top + Math.Max(0, (area.Height - Height) / 2));
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_LAYERED;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Redraw();
    }

    protected override void OnPaint(PaintEventArgs e) { }

    void Redraw()
    {
        if (!IsHandleCreated) return;
        using var bmp = RenderBitmap();
        if (Size != bmp.Size) Size = bmp.Size;
        Glass.Push(this, bmp);
    }

    Bitmap RenderBitmap()
    {
        var bmp = Glass.NewCanvas(Layout(null), out var g);
        using (g) Layout(g);
        return bmp;
    }

    /// <summary>Build helper: a picture of this window without showing it.</summary>
    public Bitmap Snapshot() => RenderBitmap();

    // ------------------------------------------------------------------ input

    string? HitTest(Point p) => _hits.FirstOrDefault(h => h.Value.Contains(p)).Key;

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var h = HitTest(e.Location);
        Cursor = h != null ? Cursors.Hand : Cursors.Default;
        if (h != _hover) { _hover = h; Redraw(); }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != null) { _hover = null; Redraw(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (Picking && e.Button is MouseButtons.Middle or MouseButtons.XButton1 or MouseButtons.XButton2)
        {
            var k = e.Button switch { MouseButtons.Middle => Keys.MButton, MouseButtons.XButton1 => Keys.XButton1, _ => Keys.XButton2 };
            SetKey(new HotKey(k, ModifierKeys.HasFlag(Keys.Control), ModifierKeys.HasFlag(Keys.Shift), ModifierKeys.HasFlag(Keys.Alt)));
            return;
        }
        if (e.Button != MouseButtons.Left) return;
        // clicking anywhere but another key stops picking
        if (Picking && HitTest(e.Location)?.StartsWith("key") != true) StopPicking();
        _pressed = HitTest(e.Location);
        if (_pressed == null)
        {
            // anywhere else: drag the window
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
        }
        else Redraw();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        var was = _pressed;
        _pressed = null;
        if (was != null && was == HitTest(e.Location)) Act(was);
        else Redraw();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (Picking)
        {
            e.Handled = e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Escape) StopPicking();
            else if (HotKey.Usable(e.KeyCode)) SetKey(new HotKey(e.KeyCode, e.Control, e.Shift, e.Alt));
            // a modifier on its own: wait for the key that goes with it
            return;
        }
        if (e.KeyCode is Keys.Enter or Keys.Escape) Close();
        else if (e.KeyCode == Keys.Space) Act("startup");
    }

    // while picking, every key (Tab, the arrows, Alt+..., F10) goes to the picker instead of Windows
    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (Picking)
        {
            OnKeyDown(new KeyEventArgs(keyData));
            return true;
        }
        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        StopPicking();
    }

    void StopPicking()
    {
        if (!Picking) return;
        _picking = -1;
        Redraw();
    }

    void SetKey(HotKey k)
    {
        var i = _picking;
        _picking = -1;
        // already used by another action: those two swap
        var j = Array.IndexOf(_keys, k);
        if (j >= 0 && j != i) _keys[j] = _keys[i];
        _keys[i] = k;
        KeysChanged?.Invoke((HotKey[])_keys.Clone());
        Redraw();
    }

    void Act(string what)
    {
        if (what.StartsWith("key"))
        {
            var i = what[3] - '0';
            _picking = _picking == i ? -1 : i;
            Redraw();
            return;
        }
        switch (what)
        {
            case "resetkeys":
                HotKey.Defaults.CopyTo(_keys, 0);
                _picking = -1;
                KeysChanged?.Invoke((HotKey[])_keys.Clone());
                break;
            case "done":
            case "close":
                Close();
                return;
            case "startup":
                StartWithWindowsChecked = !StartWithWindowsChecked;
                break;
            case "again":
                _checks = [SetupChecks.Replays(_replayDir), SetupChecks.WindowMode()];
                break;
        }
        Redraw();
    }

    // ------------------------------------------------------------------ drawing

    void EnsureFonts()
    {
        if (_fontsFor == K) return;
        _fontsFor = K;
        Font F(string family, float px, FontStyle style)
        {
            try { return new Font(family, px * K, style, GraphicsUnit.Pixel); }
            catch (ArgumentException) { return new Font("Segoe UI", px * K, style, GraphicsUnit.Pixel); }
        }
        _title = F("Bahnschrift", 26, FontStyle.Bold);
        _sub = F("Segoe UI", 13.5f, FontStyle.Regular);
        _section = F("Segoe UI Semibold", 11, FontStyle.Regular);
        _bold = F("Segoe UI Semibold", 14, FontStyle.Regular);
        _text = F("Segoe UI", 13, FontStyle.Regular);
        _key = F("Segoe UI Semibold", 12, FontStyle.Regular);
        _btn = F("Segoe UI Semibold", 13.5f, FontStyle.Regular);
        _symbol = F("Segoe UI", 15, FontStyle.Bold);
    }

    static readonly StringFormat Typo = MakeFormat(wrap: false), Wrap = MakeFormat(wrap: true);
    static StringFormat MakeFormat(bool wrap)
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip;
        if (!wrap) f.FormatFlags |= StringFormatFlags.NoWrap;
        return f;
    }
    static readonly Graphics MeasureG = MakeMeasureG();
    static Graphics MakeMeasureG()
    {
        var g = Graphics.FromImage(new Bitmap(1, 1));
        g.TextRenderingHint = Glass.TextHint;
        return g;
    }

    static int TextW(string s, Font f) => (int)Math.Ceiling(MeasureG.MeasureString(s, f, PointF.Empty, Typo).Width);

    static void Str(Graphics? g, string s, Font f, Color c, float x, float y)
    {
        if (g == null) return;
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, x, y, Typo);
    }

    /// <summary>Wrapped paragraph; returns its height.</summary>
    static int Para(Graphics? g, string s, Font f, Color c, int x, int y, int w)
    {
        var h = (int)Math.Ceiling(MeasureG.MeasureString(s, f, w, Wrap).Height);
        if (g != null)
        {
            using var b = new SolidBrush(c);
            g.DrawString(s, f, b, new RectangleF(x, y, w, h + 4), Wrap);
        }
        return h;
    }

    static void Fill(Graphics? g, RectangleF r, float radius, Color fill, Color? border = null)
    {
        if (g == null) return;
        var smooth = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using var path = Glass.Rounded(r, radius);
        using (var b = new SolidBrush(fill)) g.FillPath(b, path);
        if (border is Color bc) { using var p = new Pen(bc, 1); g.DrawPath(p, path); }
        g.SmoothingMode = smooth;
    }

    static readonly Color Line = Color.FromArgb(26, 255, 255, 255);

    /// <summary>Lays out (and draws when g is set) the whole window; returns its size.</summary>
    Size Layout(Graphics? g)
    {
        EnsureFonts();
        _hits.Clear();
        int w = Px(560), pad = Px(28), inner = w - 2 * pad;
        int y = Px(24);

        // first pass only measures; the panel needs the final height, so measure, then draw
        if (g != null)
        {
            var size = Layout(null);
            Glass.Panel(g, size, Px(16), 238, 226, Glass.Edge);
            _hits.Clear();
        }

        // close (x), top right
        var close = new Rectangle(w - Px(14) - Px(30), Px(14), Px(30), Px(30));
        _hits["close"] = close;
        if (g != null)
        {
            if (_hover == "close") Fill(g, close, Px(8), Color.FromArgb(30, 255, 255, 255));
            using var p = new Pen(_hover == "close" ? OverlayForm.Fg : OverlayForm.Dim, Math.Max(1.5f, Px(1.6f)));
            var smooth = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            float c = Px(5.5f), cx = close.X + close.Width / 2f, cy = close.Y + close.Height / 2f;
            g.DrawLine(p, cx - c, cy - c, cx + c, cy + c);
            g.DrawLine(p, cx - c, cy + c, cx + c, cy - c);
            g.SmoothingMode = smooth;
        }

        // title + intro
        Str(g, "Chest Counter", _title, OverlayForm.Gold, pad, y);
        y += _title.Height + Px(2);
        y += Para(g, "Counts the chests you open or break and the season treasures you get, straight from your " +
                     "Fortnite replays. It runs quietly in the tray, next to the clock.", _sub, OverlayForm.Dim, pad, y, inner);

        // section title and a line; optionally a small text button at the right end
        void Section(string name, string? linkId = null, string? linkText = null)
        {
            y += Px(20);
            Str(g, name, _section, OverlayForm.Dim, pad, y);
            var tw = TextW(name, _section);
            var lineEnd = pad + inner;
            if (linkId != null && linkText != null)
            {
                var lw = TextW(linkText, _section);
                var r = new Rectangle(pad + inner - lw - Px(6), y - Px(4), lw + Px(12), _section.Height + Px(8));
                _hits[linkId] = r;
                Str(g, linkText, _section, _hover == linkId ? OverlayForm.Fg : OverlayForm.Gold, pad + inner - lw, y);
                lineEnd = r.X - Px(6);
            }
            if (g != null) using (var b = new SolidBrush(Line)) g.FillRectangle(b, pad + tw + Px(10), y + _section.Height / 2, lineEnd - pad - tw - Px(10), 1);
            y += _section.Height + Px(10);
        }

        // ---- checks ----
        Section("CHECKS");
        foreach (var c in _checks)
        {
            var color = c.State switch
            {
                SetupChecks.State.Ok => OverlayForm.Green,
                SetupChecks.State.Warn => OverlayForm.Gold,
                _ => OverlayForm.Red,
            };
            var d = Px(26);
            if (g != null)
            {
                var smooth = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var glow = new SolidBrush(Color.FromArgb(40, color))) g.FillEllipse(glow, pad, y, d, d);
                using (var ring = new Pen(Color.FromArgb(150, color), 1)) g.DrawEllipse(ring, pad + 0.5f, y + 0.5f, d - 1, d - 1);
                using var pen = new Pen(color, Px(2.2f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
                float cx = pad + d / 2f, cy = y + d / 2f, u = d / 26f;
                switch (c.State)
                {
                    case SetupChecks.State.Ok:
                        g.DrawLines(pen, [new PointF(cx - 6 * u, cy), new PointF(cx - 2 * u, cy + 4.5f * u), new PointF(cx + 6.5f * u, cy - 5 * u)]);
                        break;
                    case SetupChecks.State.Warn:
                        g.DrawLine(pen, cx, cy - 6 * u, cx, cy + 1.5f * u);
                        using (var dot = new SolidBrush(color)) g.FillEllipse(dot, cx - 1.6f * u, cy + 4.2f * u, 3.2f * u, 3.2f * u);
                        break;
                    default:
                        g.DrawLine(pen, cx - 5 * u, cy - 5 * u, cx + 5 * u, cy + 5 * u);
                        g.DrawLine(pen, cx - 5 * u, cy + 5 * u, cx + 5 * u, cy - 5 * u);
                        break;
                }
                g.SmoothingMode = smooth;
            }
            int tx = pad + d + Px(14), tw = inner - d - Px(14);
            var top = y;
            y += Px(1);
            y += Para(g, c.Title, _bold, c.State == SetupChecks.State.Ok ? OverlayForm.Fg : color, tx, y, tw) + Px(2);
            y += Para(g, c.Detail, _text, OverlayForm.Dim, tx, y, tw);
            y = Math.Max(y, top + d) + Px(12);
        }
        y -= Px(12);

        // ---- keys ----
        var customKeys = !_keys.SequenceEqual(HotKey.Defaults);
        Section("KEYS  (work while you play)", customKeys ? "resetkeys" : null, "RESET TO F8");
        string[] actions = ["Hide / show the overlay", "Move it: drag with the mouse, press again to lock", "Switch view: full, compact, graph"];
        // the key column fits the widest key, e.g. "Ctrl + Shift + Mouse 4"
        int CapW(string s) => Math.Max(Px(30), TextW(s, _key) + Px(18));
        int plusW = Px(4) + TextW("+", _key) + Px(4);
        int KeysW(string[] parts) => parts.Sum(CapW) + plusW * (parts.Length - 1);
        var descX = pad + Math.Max(Px(132), _keys.Max(k => KeysW(k.Parts())) + Px(22));
        for (var row = 0; row < HotKey.Count; row++)
        {
            var id = "key" + row;
            var capH = Px(26);
            var picking = _picking == row;
            var hover = _hover == id && !picking;
            var what = picking ? "Press the new key or a mouse side button. Esc cancels." : actions[row];
            var descW = pad + inner - descX;
            var descH = Para(null, what, _text, OverlayForm.Soft, descX, y, descW);
            var keyRowH = Math.Max(capH, descH);
            _hits[id] = new Rectangle(pad - Px(6), y - Px(3), inner + Px(12), keyRowH + Px(6));
            if (picking || hover) Fill(g, _hits[id], Px(8), Color.FromArgb(picking ? 26 : 14, 255, 255, 255), picking ? Color.FromArgb(140, OverlayForm.Gold) : null);
            var keys = picking ? ["press a key"] : _keys[row].Parts();
            var x = pad;
            for (var i = 0; i < keys.Length; i++)
            {
                if (i > 0)
                {
                    Str(g, "+", _key, OverlayForm.Dim, x + Px(4), y + (capH - _key.Height) / 2f);
                    x += plusW;
                }
                var cw = CapW(keys[i]);
                Fill(g, new RectangleF(x, y, cw, capH), Px(6), Color.FromArgb(22, 255, 255, 255),
                    picking ? OverlayForm.Gold : Color.FromArgb(hover ? 110 : 55, 255, 255, 255));
                if (g != null) using (var under = new SolidBrush(Color.FromArgb(40, 0, 0, 0))) g.FillRectangle(under, x + Px(4), y + capH - Px(1), cw - Px(8), Px(1));
                Str(g, keys[i], _key, picking ? OverlayForm.Gold : OverlayForm.Fg, x + (cw - TextW(keys[i], _key)) / 2f, y + (capH - _key.Height) / 2f);
                x += cw;
            }
            Para(g, what, _text, picking ? OverlayForm.Gold : OverlayForm.Soft, descX, descH < capH ? y + (capH - descH) / 2 : y, descW);
            y += keyRowH + Px(8);
        }
        y += Para(g, "Click a key to change it. Pick one you don't use in Fortnite. Right-click the tray icon for the full report, " +
                     "the graph, Start with Windows and Exit.", _text, OverlayForm.Dim, pad, y + Px(2), inner);

        // ---- good to know ----
        Section("GOOD TO KNOW");
        foreach (var s in new[]
                 {
                     "The first start reads your last ~100 games. That takes about a minute; the overlay shows the progress.",
                     "After that, each game is counted a few seconds after you leave the match.",
                     "Custom games (fewer than 10 players) are left out.",
                 })
        {
            if (g != null)
            {
                var smooth = g.SmoothingMode;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using var b = new SolidBrush(OverlayForm.Gold);
                g.FillEllipse(b, pad + Px(2), y + _text.Height / 2f - Px(3), Px(6), Px(6));
                g.SmoothingMode = smooth;
            }
            y += Para(g, s, _text, OverlayForm.Soft, pad + Px(18), y, inner - Px(18)) + Px(6);
        }

        // ---- bottom: start with Windows + buttons ----
        y += Px(10);
        if (g != null) using (var b = new SolidBrush(Line)) g.FillRectangle(b, pad, y, inner, 1);
        y += Px(18);

        var box = Px(20);
        var label = "Start with Windows";
        var rowH = Px(36);
        var boxY = y + (rowH - box) / 2;
        var hitW = box + Px(10) + TextW(label, _bold);
        _hits["startup"] = new Rectangle(pad - Px(4), y, hitW + Px(8), rowH);
        Fill(g, new RectangleF(pad, boxY, box, box), Px(5),
            StartWithWindowsChecked ? OverlayForm.Gold : Color.FromArgb(_hover == "startup" ? 30 : 16, 255, 255, 255),
            StartWithWindowsChecked ? null : Color.FromArgb(90, 255, 255, 255));
        if (g != null && StartWithWindowsChecked)
        {
            var smooth = g.SmoothingMode;
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using var pen = new Pen(OverlayForm.Bg, Px(2.2f)) { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round };
            float u = box / 20f, bx = pad, by = boxY;
            g.DrawLines(pen, [new PointF(bx + 5 * u, by + 10.5f * u), new PointF(bx + 8.5f * u, by + 14 * u), new PointF(bx + 15 * u, by + 6.5f * u)]);
            g.SmoothingMode = smooth;
        }
        var lx = pad + box + Px(10);
        Str(g, label, _bold, OverlayForm.Fg, lx, y + (rowH - _bold.Height) / 2f);
        y += rowH + Px(12);

        // buttons, right-aligned: [Check again] [Done]
        var bh = Px(38);
        int Btn(string id, string text, int right, bool primary)
        {
            var bw = TextW(text, _btn) + Px(36);
            var r = new Rectangle(right - bw, y, bw, bh);
            _hits[id] = r;
            var hover = _hover == id;
            var down = _pressed == id && hover;
            Fill(g, r, Px(9),
                primary ? (down ? ColorTranslator.FromHtml("#e6ad2a") : hover ? ColorTranslator.FromHtml("#ffd166") : OverlayForm.Gold)
                        : Color.FromArgb(down ? 44 : hover ? 32 : 18, 255, 255, 255),
                primary ? null : Color.FromArgb(70, 255, 255, 255));
            Str(g, text, _btn, primary ? OverlayForm.Bg : OverlayForm.Fg, r.X + (bw - TextW(text, _btn)) / 2f, r.Y + (bh - _btn.Height) / 2f);
            return r.X;
        }
        var nextRight = Btn("done", "Done", w - pad, primary: true);
        Btn("again", "Check again", nextRight - Px(10), primary: false);
        // the note goes in the gap left of the buttons
        Para(g, "Start with Windows is recommended, so no game gets missed.", _text, OverlayForm.Dim, pad, y + (bh - _text.Height * 2) / 2, Math.Max(Px(120), nextRight - Px(10) - TextW("Check again", _btn) - Px(36) - pad - Px(20)));
        y += bh + Px(24);

        return new Size(w, y);
    }
}

