using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace ChestCounter;

/// <summary>The treasure chest picture used for the window, tray and exe icon. Drawn in code so it's crisp at any size.</summary>
static class ChestArt
{
    static readonly Color Wood = Color.FromArgb(0x9A, 0x5E, 0x2A);
    static readonly Color WoodDark = Color.FromArgb(0x6E, 0x40, 0x1C);
    static readonly Color Lid = Color.FromArgb(0xB4, 0x74, 0x36);
    static readonly Color Gold = Color.FromArgb(0xF5, 0xB8, 0x41);
    static readonly Color Outline = Color.FromArgb(0x2E, 0x1A, 0x0B);

    public static void Draw(Graphics g, RectangleF r)
    {
        var state = g.Save();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TranslateTransform(r.X, r.Y);
        var s = Math.Min(r.Width, r.Height);
        float X(double v) => (float)(v * s);

        var stroke = Math.Max(1f, s / 22f);
        using var outline = new Pen(Outline, stroke) { LineJoin = LineJoin.Round };

        // body
        var body = new RectangleF(X(0.08), X(0.44), X(0.84), X(0.46));
        using (var p = Rounded(body, X(0.06)))
        using (var b = new LinearGradientBrush(body, Wood, WoodDark, LinearGradientMode.Vertical))
        {
            g.FillPath(b, p);
            g.DrawPath(outline, p);
        }

        // lid: rounded top
        var lid = new RectangleF(X(0.08), X(0.14), X(0.84), X(0.34));
        using (var p = new GraphicsPath())
        {
            var rr = X(0.2);
            p.AddArc(lid.X, lid.Y, rr * 2, rr * 2, 180, 90);
            p.AddArc(lid.Right - rr * 2, lid.Y, rr * 2, rr * 2, 270, 90);
            p.AddLine(lid.Right, lid.Bottom, lid.X, lid.Bottom);
            p.CloseFigure();
            using var b = new LinearGradientBrush(lid, Lid, Wood, LinearGradientMode.Vertical);
            g.FillPath(b, p);
            g.DrawPath(outline, p);
        }

        // gold straps and rim
        using (var gold = new SolidBrush(Gold))
        {
            g.FillRectangle(gold, X(0.22), X(0.16), X(0.09), X(0.73));
            g.FillRectangle(gold, X(0.69), X(0.16), X(0.09), X(0.73));
            g.FillRectangle(gold, X(0.08), X(0.43), X(0.84), X(0.07));

            // lock
            var lk = new RectangleF(X(0.41), X(0.38), X(0.18), X(0.22));
            using var lp = Rounded(lk, X(0.04));
            g.FillPath(gold, lp);
            g.DrawPath(outline, lp);
        }
        if (s >= 20)
        {
            using var hole = new SolidBrush(Outline);
            g.FillEllipse(hole, X(0.475), X(0.45), X(0.05), X(0.06));
        }
        g.Restore(state);
    }

    public static Bitmap Bitmap(int size)
    {
        var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        var pad = size >= 32 ? size * 0.02f : 0;
        Draw(g, new RectangleF(pad, pad, size - pad * 2, size - pad * 2));
        return bmp;
    }

    public static Icon Icon(int size)
    {
        using var bmp = Bitmap(size);
        var h = bmp.GetHicon();
        // Clone so the icon owns its own handle, then free the temporary one.
        using var tmp = System.Drawing.Icon.FromHandle(h);
        var icon = (Icon)tmp.Clone();
        Native.DestroyIcon(h);
        return icon;
    }

    /// <summary>Writes a multi-size .ico (PNG entries). Used once to make the exe icon: ChestCounter.exe --make-icon chest.ico</summary>
    public static void WriteIco(string path)
    {
        int[] sizes = [16, 20, 24, 32, 40, 48, 64, 256];
        var pngs = sizes.Select(sz =>
        {
            using var bmp = Bitmap(sz);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            return ms.ToArray();
        }).ToList();

        using var w = new BinaryWriter(File.Create(path));
        w.Write((short)0); w.Write((short)1); w.Write((short)sizes.Length);
        var offset = 6 + 16 * sizes.Length;
        for (var i = 0; i < sizes.Length; i++)
        {
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)(sizes[i] >= 256 ? 0 : sizes[i]));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((short)1); w.Write((short)32);
            w.Write(pngs[i].Length); w.Write(offset);
            offset += pngs[i].Length;
        }
        foreach (var p in pngs) w.Write(p);
    }

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        var d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}
