namespace ChestCounter;

/// <summary>
/// The in-game overlay: a small see-through panel that stays on top of the game, styled like your Python
/// chest counter. Clicks go straight through it to the game unless move mode is on.
/// Works over Windowed Fullscreen / Borderless (not exclusive Fullscreen).
/// </summary>
sealed class OverlayForm : Form
{
    public static readonly Color Bg = ColorTranslator.FromHtml("#0f1216");
    public static readonly Color Fg = ColorTranslator.FromHtml("#e9edf2");
    public static readonly Color Dim = ColorTranslator.FromHtml("#8b95a1");
    public static readonly Color Gold = ColorTranslator.FromHtml("#ffc53d");
    public static readonly Color Green = ColorTranslator.FromHtml("#4ade80");
    public static readonly Color Red = ColorTranslator.FromHtml("#f87171");
    /// <summary>Mode names in the table: a bit lighter than <see cref="Dim"/>.</summary>
    public static readonly Color Soft = ColorTranslator.FromHtml("#b8c0ca");
    static readonly Color LineColor = Color.FromArgb(26, 255, 255, 255);

    // Glass look: only the background is see-through, text stays solid.
    const int GlassTopAlpha = 196, GlassBottomAlpha = 170;   // 77% -> 67%
    static readonly Color GlassTop = Color.FromArgb(GlassTopAlpha, 22, 26, 33);
    static readonly Color GlassBottom = Color.FromArgb(GlassBottomAlpha, 12, 14, 18);
    static readonly Color GlassEdge = Color.FromArgb(28, 255, 255, 255);

    /// <summary>A table: optional small header row, then rows of label + right-aligned values.</summary>
    /// <remarks>KeyColumn: the one value column that matters most (bold white); the others are drawn grey.</remarks>
    public sealed record Table(string[]? Header, List<TableRow> Rows, int KeyColumn = -1)
    {
        public bool IsKey(TableRow r, int i) => r.KeyCell >= 0 ? i == r.KeyCell : KeyColumn < 0 ? r.Bold : i == KeyColumn;
    }
    public sealed record TableRow(string Label, string[] Cells, Color? Color = null, bool Bold = true,
        bool LineBefore = false, int KeyCell = -1, Color? LabelColor = null);

    /// <summary>Everything the overlay shows. Built by <see cref="OverlayModel"/>.</summary>
    public sealed class View
    {
        public string Clock = "";
        /// <summary>Top row: big value + small label under it.</summary>
        public List<(string Value, string Label, Color? Color)> Headline = new();
        /// <summary>Second row of numbers, a bit smaller.</summary>
        public List<(string Value, string Label, Color? Color)> Headline2 = new();
        public Color ClockColor = Dim;
        public string Count = "0";
        public Color CountColor = Gold;
        public string? Flash;
        public Color FlashColor = Green;
        public string AllTime = "";
        public string SessionSub = "";
        public string AllTimeSub = "";
        /// <summary>Tables, separated by thin lines.</summary>
        public List<Table> Tables = new();
        /// <summary>Status line at the bottom (only when something is happening).</summary>
        public string? Status;
        public Color StatusColor = Dim;
        public string? Hint;
        public Color HintColor = Dim;
        /// <summary>Small dot in the top-right corner: gold = in a game, green = reading the game that just ended.</summary>
        public Color? Dot;

        /// <summary>Set = show the graph instead of the numbers: chests in each game, oldest first.</summary>
        public List<int>? Graph;
        /// <summary>Average of the last <see cref="GraphWindow"/> games at each point (same length as Graph).</summary>
        public List<double> GraphTrend = new();
        public int GraphWindow = 10;
        public double GraphAvg;
    }

    const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine | TextFormatFlags.NoClipping;

    View _view = new();
    bool _moving;
    Font _fBig = null!, _fMid = null!, _fHead = null!, _fText = null!, _fValue = null!, _fCount = null!, _fFlash = null!, _fSmall = null!;
    float _fontsFor;

    public event Action? MoveEnded;
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public ContextMenuStrip? MoveMenu { get; set; }

    public OverlayForm()
    {
        Text = "Chest Counter overlay";
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        TopMost = true;
        BackColor = Bg;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint, true);
        UpdateSize();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Redraw();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (Visible) Redraw();
    }

    /// <summary>
    /// Draws the overlay into a see-through picture and hands it to Windows (per-pixel alpha), so the
    /// background can be glassy while the numbers stay fully solid.
    /// </summary>
    void Redraw()
    {
        if (!IsHandleCreated) return;
        using var bmp = RenderBitmap();
        var screen = Native.GetDC(IntPtr.Zero);
        var mem = Native.CreateCompatibleDC(screen);
        var hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        var old = Native.SelectObject(mem, hbmp);
        try
        {
            var dst = new Native.POINT { X = Left, Y = Top };
            var size = new Native.SIZE { CX = bmp.Width, CY = bmp.Height };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA };
            Native.UpdateLayeredWindow(Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(mem, old);
            Native.DeleteObject(hbmp);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }

    Bitmap RenderBitmap()
    {
        var size = Render(null);
        var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        Render(g);
        return bmp;
    }

    float K => DeviceDpi / 96f;
    int Px(float v) => (int)Math.Round(v * K);

    public bool Moving => _moving;

    public void SetView(View v)
    {
        _view = v;
        UpdateSize();
        Redraw();
    }

    /// <summary>Move mode: gold border, draggable, right-click menu. Otherwise clicks pass through to the game.</summary>
    public void SetMoving(bool on)
    {
        if (_moving == on) return;
        _moving = on;
        UpdateStyles();
        BringToTop();
        Redraw();
        if (!on) MoveEnded?.Invoke();
    }

    public void BringToTop()
    {
        if (IsHandleCreated && Visible)
            Native.SetWindowPos(Handle, Native.HWND_TOPMOST, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= Native.WS_EX_TOOLWINDOW | Native.WS_EX_LAYERED;
            if (!_moving) cp.ExStyle |= Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE;
            return cp;
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (!_moving) return;
        if (e.Button == MouseButtons.Left)
        {
            Native.ReleaseCapture();
            Native.SendMessage(Handle, Native.WM_NCLBUTTONDOWN, Native.HTCAPTION, IntPtr.Zero);
        }
        else if (e.Button == MouseButtons.Right)
        {
            MoveMenu?.Show(Cursor.Position);
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        UpdateSize();
        Redraw();
    }

    // ------------------------------------------------------------------ drawing

    void EnsureFonts()
    {
        if (_fontsFor == K) return;
        _fontsFor = K;
        Font F(string family, float pt, bool bold)
        {
            // Sizes in points like the Python overlay (9pt text, 10pt values, 30pt count), at 96 dpi * monitor scale.
            var px = pt * 96f / 72f * K;
            try { return new Font(family, px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
            catch (ArgumentException) { return new Font("Segoe UI", px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel); }
        }
        _fHead = F("Segoe UI", 9, true);
        _fText = F("Segoe UI", 9, false);
        _fValue = F("Segoe UI", 10, true);
        _fCount = F("Bahnschrift", 30, true);
        _fFlash = F("Bahnschrift", 14, true);
        _fBig = F("Bahnschrift", 26, true);
        _fMid = F("Bahnschrift", 15, true);
        _fSmall = F("Segoe UI", 8, false);
    }

    readonly Dictionary<(string, int), Font> _sized = new();

    /// <summary>The same font at another pixel size (rounded to whole pixels, cached).</summary>
    Font Sized(Font f, float px)
    {
        var key = (f.FontFamily.Name + (f.Bold ? "b" : ""), (int)Math.Round(px));
        if (key.Item2 == (int)Math.Round(f.Size)) return f;
        if (!_sized.TryGetValue(key, out var font))
            _sized[key] = font = new Font(f.FontFamily, key.Item2, f.Style, GraphicsUnit.Pixel);
        return font;
    }

    // Text is drawn with GDI+ (anti-aliased), which keeps solid text on a see-through background.
    static readonly StringFormat Typo = MakeTypo();
    static StringFormat MakeTypo()
    {
        var f = (StringFormat)StringFormat.GenericTypographic.Clone();
        f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap | StringFormatFlags.NoClip;
        return f;
    }
    static readonly Graphics MeasureG = MakeMeasureG();
    static Graphics MakeMeasureG()
    {
        var g = Graphics.FromImage(new Bitmap(1, 1));
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
        return g;
    }

    /// <summary>Ascent + descent in pixels: the text's own height, without the extra line gap under it.</summary>
    static int GlyphHeight(Font f)
    {
        var fam = f.FontFamily;
        var em = fam.GetEmHeight(f.Style);
        return (int)Math.Ceiling((fam.GetCellAscent(f.Style) + fam.GetCellDescent(f.Style)) * f.Size / em);
    }

    static Size Measure(string s, Font f)
    {
        if (string.IsNullOrEmpty(s)) return Size.Empty;
        var sz = MeasureG.MeasureString(s, f, PointF.Empty, Typo);
        return new Size((int)Math.Ceiling(sz.Width), f.Height);
    }

    void UpdateSize()
    {
        var size = Render(null);
        if (ClientSize != size) ClientSize = size;
    }

    // Windows ignores WM_PAINT for this kind of window; Redraw() does the drawing.
    protected override void OnPaint(PaintEventArgs e) { }

    static System.Drawing.Drawing2D.GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2;
        var p = new System.Drawing.Drawing2D.GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Draws the overlay (when g is set) and returns its size in pixels.</summary>
    Size Render(Graphics? g)
    {
        // The graph view takes exactly the space the numbers would, so switching doesn't move anything.
        var size = RenderNumbers(null);
        if (g == null) return size;

        // glass background: rounded, see-through gradient, thin light edge (gold while moving)
        var smooth = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, size.Width - 1, size.Height - 1);
        using (var path = Rounded(rect, Px(12)))
        {
            using (var bg = new System.Drawing.Drawing2D.LinearGradientBrush(new RectangleF(0, 0, size.Width, size.Height), GlassTop, GlassBottom, 70f))
                g.FillPath(bg, path);
            using var edge = new Pen(_moving ? Gold : GlassEdge, _moving ? Px(2) : 1);
            g.DrawPath(edge, path);
        }
        g.SmoothingMode = smooth;

        if (_view.Graph == null) RenderNumbers(g);
        else RenderGraph(g, size);

        if (_view.Dot is Color dot)
        {
            // small dot with a soft glow, top-right corner
            var d = Px(7);
            float cx = size.Width - Px(13), cy = Px(13);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            for (var i = 3; i >= 1; i--)
            {
                var r = d / 2f + i * Px(2);
                using var glow = new SolidBrush(Color.FromArgb(28, dot));
                g.FillEllipse(glow, cx - r, cy - r, r * 2, r * 2);
            }
            using var b = new SolidBrush(dot);
            g.FillEllipse(b, cx - d / 2f, cy - d / 2f, d, d);
            g.SmoothingMode = smooth;
        }
        return size;
    }

    static readonly Color BarColor = ColorTranslator.FromHtml("#3d4856");
    /// <summary>Chests per game as bars, with a gold line for the recent average and a dashed one for the all-time average.</summary>
    void RenderGraph(Graphics? g, Size size)
    {
        if (g == null) return;
        var v = _view;
        var data = v.Graph!;
        var trend = v.GraphTrend;
        int border = Px(2), padX = Px(12), padY = Px(8);
        int x0 = border + padX, right = size.Width - padX - border;
        int top = border + padY, bottom = size.Height - padY - border;

        // status + hint at the bottom, like the numbers view
        var hintLines = v.Hint?.Split('\n') ?? [];
        var y = bottom - hintLines.Length * _fSmall.Height - (hintLines.Length > 0 ? Px(4) : 0)
                - (v.Status != null ? Px(5) + _fText.Height : 0);
        var footTop = y;
        if (v.Status != null)
        {
            y += Px(5);
            DrawStr(g, v.Status, _fText, v.StatusColor, x0, y);
            y += _fText.Height;
        }
        if (hintLines.Length > 0)
        {
            y += Px(4);
            foreach (var l in hintLines) { DrawStr(g, l, _fSmall, v.HintColor, x0, y); y += _fSmall.Height; }
        }

        // title row
        var fTitle = Sized(_fMid, _fMid.Size * 0.8f);
        DrawStr(g, "CHESTS PER GAME", fTitle, Fg, x0, top);
        if (trend.Count > 0)
        {
            // "last 10:" as a label, the number in gold like the line it belongs to
            var num = trend[^1].ToString("F1");
            var numW = Measure(num, fTitle).Width;
            DrawRight(g, num, fTitle, Gold, right, top);
            DrawRight(g, $"last {v.GraphWindow}  ", _fText, Dim, right - numW, top + fTitle.Height - _fText.Height - Px(1));
        }
        var plotTop = top + fTitle.Height + Px(10);

        // legend row under the plot
        var legendY = footTop - _fSmall.Height;
        var plotBottom = legendY - Px(8);
        if (data.Count == 0)
        {
            DrawStr(g, "no games yet", _fText, Dim, x0, plotTop);
            return;
        }

        // y axis: gridlines every 20 (or 40) chests
        var max = Math.Max(data.Max(), 1);
        var step = max > 80 ? 40 : 20;
        var yMax = (int)Math.Ceiling(max / (double)step) * step;
        var axisW = Measure(yMax.ToString(), _fSmall).Width;
        int plotX = x0 + axisW + Px(6), plotW = right - plotX, plotH = plotBottom - plotTop;
        float Y(double value) => plotBottom - (float)(value / yMax * plotH);
        for (var t = 0; t <= yMax; t += step)
        {
            var gy = (int)Math.Round(Y(t));
            HLine(g, plotX, right, gy);
            DrawRight(g, t.ToString(), _fSmall, Dim, x0 + axisW, gy - _fSmall.Height / 2);
        }

        // bars
        var n = data.Count;
        int X(int i) => plotX + (int)((long)i * plotW / n);
        using (var bar = new SolidBrush(BarColor))
        {
            for (var i = 0; i < n; i++)
            {
                int xa = X(i), xb = X(i + 1);
                var w = Math.Max(1, xb - xa - (xb - xa >= 3 ? 1 : 0));
                var top_ = (int)Math.Round(Y(data[i]));
                g.FillRectangle(bar, xa, top_, w, plotBottom - top_);
            }
        }

        var oldSmooth = g.SmoothingMode;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        // all-time average: dashed
        using (var pen = new Pen(Dim, Math.Max(1, Px(1))) { DashPattern = [4, 3] })
            g.DrawLine(pen, plotX, Y(v.GraphAvg), right, Y(v.GraphAvg));
        // recent average: gold line through the middle of each bar
        if (trend.Count == n && n >= 2)
        {
            var pts = trend.Select((t, i) => new PointF((X(i) + X(i + 1)) / 2f, Y(t))).ToArray();
            using var pen = new Pen(Gold, Px(2)) { LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            g.DrawLines(pen, pts);
        }
        g.SmoothingMode = oldSmooth;

        // legend: three items spread across
        var items = new (string Text, int Kind)[]
        {
            ($"{n} games", 0), ($"last-{v.GraphWindow} avg", 1), ($"all-time {v.GraphAvg:F1}", 2),
        };
        var sw = Px(12);
        var slot = (right - x0) / items.Length;
        for (var i = 0; i < items.Length; i++)
        {
            var (text, kind) = items[i];
            var w = sw + Px(4) + Measure(text, _fSmall).Width;
            var lx = i == 0 ? x0 : i == items.Length - 1 ? right - w : x0 + i * slot + (slot - w) / 2;
            var my = legendY + _fSmall.Height / 2;
            switch (kind)
            {
                case 0:
                    using (var b = new SolidBrush(BarColor)) g.FillRectangle(b, lx + Px(3), my - Px(4), Px(6), Px(8));
                    break;
                case 1:
                    using (var p = new Pen(Gold, Px(2))) g.DrawLine(p, lx, my, lx + sw, my);
                    break;
                default:
                    using (var p = new Pen(Dim, Math.Max(1, Px(1))) { DashPattern = [3, 2] }) g.DrawLine(p, lx, my, lx + sw, my);
                    break;
            }
            DrawStr(g, text, _fSmall, Dim, lx + sw + Px(4), legendY);
        }
    }

    Size RenderNumbers(Graphics? g)
    {
        EnsureFonts();
        var v = _view;
        int border = Px(2), padX = Px(12), padY = Px(8), gap = Px(14), sepPad = Px(5);
        var hintLines = v.Hint?.Split('\n') ?? [];

        // --- measure: each table's label column + value columns (right-aligned, at least 44px each) ---
        var labelW = v.Tables.SelectMany(t => t.Rows).Select(r => Measure(r.Label, _fText).Width).DefaultIfEmpty(0).Max();
        int[] ColWidths(Table t)
        {
            var n = Math.Max(t.Header?.Length - 1 ?? 0, t.Rows.Select(r => r.Cells.Length).DefaultIfEmpty(0).Max());
            var w = new int[n];
            for (var i = 0; i < n; i++)
            {
                var hw = t.Header != null && i + 1 < t.Header.Length ? Measure(t.Header[i + 1], _fSmall).Width : 0;
                var cw = t.Rows.Where(r => i < r.Cells.Length).Select(r => Measure(r.Cells[i], t.IsKey(r, i) ? _fValue : _fText).Width).DefaultIfEmpty(0).Max();
                w[i] = Math.Max(Math.Max(hw, cw), t.Rows.All(r => r.Cells.Length == 1) ? 0 : Px(44));
            }
            return w;
        }
        var cols = v.Tables.Select(ColWidths).ToList();

        var contentW = new[]
        {
            Px(200),
            v.Headline.Count * v.Headline.Select(h => Math.Max(Measure(h.Value, _fBig).Width, Measure(h.Label, _fSmall).Width) + Px(16)).DefaultIfEmpty(0).Max(),
            v.Headline2.Count * v.Headline2.Select(h => Math.Max(Measure(h.Value, _fMid).Width, Measure(h.Label, _fSmall).Width) + Px(12)).DefaultIfEmpty(0).Max(),
            Measure(v.Status ?? "", _fText).Width,
            hintLines.Select(l => Measure(l, _fSmall).Width).DefaultIfEmpty(0).Max(),
        }.Concat(cols.Select(c => labelW + gap + c.Sum() + Px(10) * Math.Max(0, c.Length - 1))).Max();

        int x0 = border + padX, right = x0 + contentW;
        int y = border + padY;

        // --- big numbers (row 1) and smaller ones (row 2), each centered in its column with a small label under it ---
        // fill: how much of its column the widest number may take; the font grows to fill it (never shrinks below f)
        // minCols: size the font as if there were at least this many columns; maxScale: never grow more than this
        void NumberRow(List<(string Value, string Label, Color? Color)> items, Font baseFont, float fill, int minCols, float maxScale)
        {
            if (items.Count == 0) return;
            var colW = contentW / items.Count;
            var widest = items.Select(h => Measure(h.Value, baseFont).Width).Max();
            var sizeW = contentW / Math.Max(items.Count, minCols);
            var scale = widest > 0 ? Math.Clamp(sizeW * fill / widest, 1f, maxScale) : 1f;
            var f = Sized(baseFont, baseFont.Size * scale);
            var numH = GlyphHeight(f);
            for (var i = 0; i < items.Count; i++)
            {
                var h = items[i];
                var mid = x0 + i * colW + colW / 2;
                DrawStr(g, h.Value, f, h.Color ?? Fg, mid - Measure(h.Value, f).Width / 2, y);
                DrawStr(g, h.Label, _fSmall, Dim, mid - Measure(h.Label, _fSmall).Width / 2, y + numH);
            }
            y += numH + _fSmall.Height;
        }
        NumberRow(v.Headline, _fBig, 0.72f, 2, 1.35f);

        // --- tables ---
        var rowH = Math.Max(_fValue.Height, _fText.Height) + Px(1);
        for (var ti = 0; ti < v.Tables.Count; ti++)
        {
            var t = v.Tables[ti];
            var w = cols[ti];
            y += sepPad;
            HLine(g, x0, right, y);
            y += Px(1) + sepPad;

            // column right edges, packed against the right side
            var rights = new int[w.Length];
            var edge = right;
            for (var i = w.Length - 1; i >= 0; i--) { rights[i] = edge; edge -= w[i] + Px(10); }

            if (t.Header != null)
            {
                if (t.Header.Length > 0) DrawStr(g, t.Header[0], _fSmall, Dim, x0, y);
                for (var i = 1; i < t.Header.Length && i - 1 < rights.Length; i++)
                    DrawRight(g, t.Header[i], _fSmall, Dim, rights[i - 1], y);
                y += _fSmall.Height + Px(1);
            }
            foreach (var r in t.Rows)
            {
                if (r.LineBefore)
                {
                    y += Px(3);
                    HLine(g, x0, right, y);
                    y += Px(1) + Px(3);
                }
                DrawStr(g, r.Label, _fText, r.LabelColor ?? Soft, x0, y + (rowH - _fText.Height) / 2);
                for (var i = 0; i < r.Cells.Length && i < rights.Length; i++)
                {
                    var key = t.IsKey(r, i);
                    var f = key ? _fValue : _fText;
                    DrawRight(g, r.Cells[i], f, r.Color ?? (key || t.KeyColumn < 0 ? Fg : Dim), rights[i], y + (rowH - f.Height) / 2);
                }
                y += rowH;
            }
        }

        // --- smaller numbers at the bottom ---
        if (v.Headline2.Count > 0)
        {
            y += sepPad;
            HLine(g, x0, right, y);
            y += Px(1) + sepPad;
            NumberRow(v.Headline2, _fMid, 0.55f, 3, 1.45f);
        }

        // --- status + hint ---
        if (v.Status != null)
        {
            y += sepPad;
            DrawStr(g, v.Status, _fText, v.StatusColor, x0, y);
            y += _fText.Height;
        }
        if (hintLines.Length > 0)
        {
            y += Px(4);
            foreach (var l in hintLines)
            {
                DrawStr(g, l, _fSmall, v.HintColor, x0, y);
                y += _fSmall.Height;
            }
        }

        y += padY + border;
        return new Size(right + padX + border, y);
    }

    static void DrawStr(Graphics? g, string s, Font f, Color c, int x, int y)
    {
        if (g == null || string.IsNullOrEmpty(s)) return;
        using var b = new SolidBrush(c);
        g.DrawString(s, f, b, x, y, Typo);
    }

    static void DrawRight(Graphics? g, string s, Font f, Color c, int right, int y)
    {
        if (g != null && !string.IsNullOrEmpty(s)) DrawStr(g, s, f, c, right - Measure(s, f).Width, y);
    }

    static void HLine(Graphics? g, int x1, int x2, int y)
    {
        if (g == null) return;
        using var b = new SolidBrush(LineColor);
        g.FillRectangle(b, x1, y, x2 - x1, 1);
    }

    /// <summary>The overlay as a picture (for checking the design without showing it).</summary>
    public Bitmap Snapshot() => RenderBitmap();
}
