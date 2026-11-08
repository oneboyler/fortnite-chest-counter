using System.Drawing.Drawing2D;

namespace ChestCounter;

/// <summary>
/// The glass look shared by the overlay and the setup window: a rounded, see-through panel drawn into a picture
/// with per-pixel transparency, so the background shows through while text stays solid.
/// </summary>
static class Glass
{
    public static readonly Color Edge = Color.FromArgb(28, 255, 255, 255);

    /// <summary>Smooth text without snapping letters to pixels (snapping caused uneven gaps like "lo ck").</summary>
    public const System.Drawing.Text.TextRenderingHint TextHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

    /// <summary>
    /// Where the visible ink of the text is centered, measured from the drawing position. Digits like "1" carry
    /// empty space on one side, so centering on the letter boxes makes numbers look off-center.
    /// </summary>
    public static float InkCenter(string s, Font f, StringFormat format)
    {
        var key = (s, f.FontFamily.Name, f.Style, f.Size);
        lock (InkCache)
        {
            if (InkCache.TryGetValue(key, out var c)) return c;
            using var p = new GraphicsPath();
            p.AddString(s, f.FontFamily, (int)f.Style, f.Size, PointF.Empty, format);
            var b = p.GetBounds();
            c = b.Width > 0 ? b.X + b.Width / 2 : 0;
            if (InkCache.Count > 2000) InkCache.Clear();
            InkCache[key] = c;
            return c;
        }
    }
    static readonly Dictionary<(string, string, FontStyle, float), float> InkCache = new();

    public static GraphicsPath Rounded(RectangleF r, float radius)
    {
        var d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>Rounded dark panel with a top-to-bottom see-through gradient and a thin edge.</summary>
    public static void Panel(Graphics g, Size size, float radius, int alphaTop, int alphaBottom, Color edge, float edgeWidth = 1)
    {
        var smooth = g.SmoothingMode;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        var rect = new RectangleF(0.5f, 0.5f, size.Width - 1, size.Height - 1);
        using var path = Rounded(rect, radius);
        using (var bg = new LinearGradientBrush(new RectangleF(0, 0, size.Width, size.Height),
                   Color.FromArgb(alphaTop, 22, 26, 33), Color.FromArgb(alphaBottom, 12, 14, 18), 70f))
            g.FillPath(bg, path);
        using var pen = new Pen(edge, edgeWidth);
        g.DrawPath(pen, path);
        g.SmoothingMode = smooth;
    }

    /// <summary>A transparent picture ready to draw on with anti-aliased text.</summary>
    public static Bitmap NewCanvas(Size size, out Graphics g)
    {
        var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        g = Graphics.FromImage(bmp);
        g.Clear(Color.Transparent);
        g.TextRenderingHint = TextHint;
        return bmp;
    }

    /// <summary>Shows the picture as the window's content (per-pixel alpha). The window must have WS_EX_LAYERED.</summary>
    public static void Push(Form form, Bitmap bmp)
    {
        if (!form.IsHandleCreated) return;
        var screen = Native.GetDC(IntPtr.Zero);
        var mem = Native.CreateCompatibleDC(screen);
        var hbmp = bmp.GetHbitmap(Color.FromArgb(0));
        var old = Native.SelectObject(mem, hbmp);
        try
        {
            var dst = new Native.POINT { X = form.Left, Y = form.Top };
            var size = new Native.SIZE { CX = bmp.Width, CY = bmp.Height };
            var src = new Native.POINT();
            var blend = new Native.BLENDFUNCTION { BlendOp = Native.AC_SRC_OVER, SourceConstantAlpha = 255, AlphaFormat = Native.AC_SRC_ALPHA };
            Native.UpdateLayeredWindow(form.Handle, screen, ref dst, ref size, mem, ref src, 0, ref blend, Native.ULW_ALPHA);
        }
        finally
        {
            Native.SelectObject(mem, old);
            Native.DeleteObject(hbmp);
            Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}
