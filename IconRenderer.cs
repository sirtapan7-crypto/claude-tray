using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace ClaudeTray;

/// <summary>
/// Draws the tray icon natively with GDI+: a rounded Claude-clay tile with the usage
/// number rendered as a vector path (outlined), so it stays crisp at the real tray
/// pixel size instead of being downscaled from a large bitmap.
/// </summary>
internal static class IconRenderer
{
    // Claude visual identity (brand color — kept for the app logo)
    private static readonly Color ClaudeClay = Color.FromArgb(217, 119, 87);
    private static readonly Color Amber      = Color.FromArgb(227, 179, 65);  // API-error
    private static readonly Color Dim         = Color.FromArgb(90, 96, 104);  // connecting
    private static readonly Color Cream       = Color.White;                  // the number
    private static readonly Color Stroke      = Color.FromArgb(20, 38, 60);   // dark outline

    // Tray status tile: a calm slate blue-gray base (orange read too much like a permanent
    // warning), with a deeper slate used while flashing at >=90%.
    private static readonly Color TileBlue  = Color.FromArgb(96, 120, 145);
    private static readonly Color BlueDeep  = Color.FromArgb(58, 76, 96);     // flash background

    // Fill-bar color: a neon green rising from the bottom, Task-Manager style — reads as a
    // distinct "in use" zone over the blue base. Turns red when the projection says usage
    // will hit 100% before the window resets.
    private static readonly Color BarFill   = Color.FromArgb(57, 230, 70);
    private static readonly Color BarDanger = Color.FromArgb(255, 35, 30);   // vivid, alarming red

    // 3D bevel edges (top-left highlight, bottom-right shadow)
    private static readonly Color BevelLight = Color.FromArgb(150, 255, 255, 255);
    private static readonly Color BevelDark  = Color.FromArgb(150, 0, 0, 0);

    public enum State { Ok, Error, Connecting }

    /// <summary>
    /// Render a square tray bitmap of side <paramref name="size"/> px. When
    /// <paramref name="danger"/> is set, the usage fill bar is drawn red instead of blue —
    /// the projection signal that usage will hit 100% before the window resets. When
    /// <paramref name="showNumber"/> is false, only the fill bar is drawn (no digits).
    /// </summary>
    public static Bitmap Render(double pct, State state, bool flash, int size, bool danger = false, bool showNumber = true)
    {
        Color bg = flash ? BlueDeep : state switch
        {
            State.Error => Amber,
            State.Connecting => Dim,
            _ => TileBlue,
        };

        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.Clear(Color.Transparent);

        // Rounded background tile (base color)
        float radius = size * 0.18f;
        using var tile = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), radius);
        using (var brush = new SolidBrush(bg))
            g.FillPath(brush, tile);

        // Vertical fill bar (Task-Manager style): a blue level rising from the bottom,
        // proportional to the percentage — 100% = whole tile, 50% = bottom half.
        if (state == State.Ok && pct > 0)
        {
            float barH = (float)(Math.Min(pct, 1.0) * size);
            using var clip = new Region(tile);
            g.Clip = clip;
            using (var barBrush = new SolidBrush(danger ? BarDanger : BarFill))
                g.FillRectangle(barBrush, 0, size - barH, size, barH);
            g.ResetClip();
        }

        // 3D beveled frame: light on top/left, dark on bottom/right → raised look.
        DrawBevel(g, new RectangleF(0, 0, size - 1, size - 1), radius, Math.Max(1f, size * 0.09f));

        if (!showNumber)
            return bmp;

        string num = ((int)Math.Round(Math.Min(pct, 1.0) * 100)).ToString();

        using var family = new FontFamily("Segoe UI");
        const int bold = (int)FontStyle.Bold;
        const float em = 100f;

        // Foreground: the digits only, large but with a clear left/right margin.
        using var text = new GraphicsPath();
        text.AddString(num, family, bold, em, new PointF(0, 0), StringFormat.GenericTypographic);
        FitToTile(text, size, size * 0.16f, size * 0.24f);

        // Dark outline then white fill — the outline keeps the number legible when tiny.
        float penW = Math.Max(1.2f, size * 0.11f);
        using (var pen = new Pen(Stroke, penW) { LineJoin = LineJoin.Round })
            g.DrawPath(pen, text);
        using (var fill = new SolidBrush(Cream))
            g.FillPath(fill, text);

        return bmp;
    }

    /// <summary>
    /// Render the application icon (used for the .exe / installer / shortcuts): the same
    /// Claude-clay beveled tile as the tray, but with a clean white spark mark instead of a
    /// usage number — a recognizable logo at any size.
    /// </summary>
    public static Bitmap RenderLogo(int size)
    {
        var bmp = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.Clear(Color.Transparent);

        float radius = size * 0.18f;
        using var tile = RoundedRect(new RectangleF(0, 0, size - 1, size - 1), radius);
        using (var brush = new SolidBrush(ClaudeClay))
            g.FillPath(brush, tile);

        DrawBevel(g, new RectangleF(0, 0, size - 1, size - 1), radius, Math.Max(1f, size * 0.05f));

        // Four-point spark, centered — legible even at 16px.
        float cx = size / 2f, cy = size / 2f;
        using var spark = Star(cx, cy, size * 0.42f, size * 0.15f, 4, -Math.PI / 2);
        using (var pen = new Pen(Stroke, Math.Max(1f, size * 0.045f)) { LineJoin = LineJoin.Round })
            g.DrawPath(pen, spark);
        using (var fill = new SolidBrush(Cream))
            g.FillPath(fill, spark);

        return bmp;
    }

    /// <summary>
    /// Render the GitHub social-preview banner (1280×640): the logo tile on a warm-dark
    /// gradient next to the project name and a one-line tagline.
    /// </summary>
    public static Bitmap RenderSocial(int w, int h)
    {
        var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        // Warm near-black gradient backdrop.
        using (var bg = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                   Color.FromArgb(32, 24, 20), Color.FromArgb(14, 10, 8), 25f))
            g.FillRectangle(bg, 0, 0, w, h);

        // Logo tile, vertically centered on the left.
        int logo = 300;
        int lx = 110, ly = (h - logo) / 2;
        using (var tile = RenderLogo(logo))
            g.DrawImage(tile, lx, ly, logo, logo);

        int tx = lx + logo + 72;
        using var titleFont = new Font("Segoe UI", 82f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var tagFont = new Font("Segoe UI Semilight", 31f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var urlFont = new Font("Segoe UI", 26f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var white = new SolidBrush(Color.FromArgb(245, 244, 242));
        using var clay = new SolidBrush(ClaudeClay);
        using var muted = new SolidBrush(Color.FromArgb(185, 182, 178));
        using var faint = new SolidBrush(Color.FromArgb(130, 126, 122));

        g.DrawString("Claude Code", titleFont, white, tx, 168);
        g.DrawString("Tray", titleFont, clay, tx, 268);

        var tagRect = new RectangleF(tx + 2, 396, w - tx - 80, 170);
        g.DrawString("Rate-limit %, burn-rate projection, and a 24h usage breakdown — always in your Windows tray.",
            tagFont, muted, tagRect);

        g.DrawString("github.com/alegauss/claude-tray", urlFont, faint, tx + 2, h - 70);

        return bmp;
    }

    /// <summary>A pointed star polygon (alternating outer/inner radius), first point at angle <paramref name="rot"/>.</summary>
    private static GraphicsPath Star(float cx, float cy, float outer, float inner, int points, double rot)
    {
        int n = points * 2;
        var pts = new PointF[n];
        for (int i = 0; i < n; i++)
        {
            double ang = rot + Math.PI * i / points;
            float rad = (i % 2 == 0) ? outer : inner;
            pts[i] = new PointF(cx + (float)(Math.Cos(ang) * rad), cy + (float)(Math.Sin(ang) * rad));
        }
        var p = new GraphicsPath();
        p.AddPolygon(pts);
        return p;
    }

    /// <summary>Scale and center a path into the tile, leaving the given horizontal/vertical margins (px).</summary>
    private static void FitToTile(GraphicsPath p, int size, float padX, float padY)
    {
        // GetBounds() on a glyph path returns the bounding box of the Bézier *control points*,
        // which bulge asymmetrically past the actual ink (e.g. the curves in "5"/"4") and throw
        // the centering off. Measure on a flattened copy so the bounds are the true ink extent.
        using var probe = (GraphicsPath)p.Clone();
        probe.Flatten();
        RectangleF b = probe.GetBounds();
        if (b.Width <= 0 || b.Height <= 0) return;
        float scale = Math.Min((size - 2 * padX) / b.Width, (size - 2 * padY) / b.Height);
        using var m = new Matrix();
        m.Translate(size / 2f, size / 2f);
        m.Scale(scale, scale);
        m.Translate(-(b.X + b.Width / 2f), -(b.Y + b.Height / 2f));
        p.Transform(m);
    }

    private static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = radius * 2;
        var p = new GraphicsPath();
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    /// <summary>
    /// Draw a 3D bevel around a rounded rect: highlight along the top+left half, shadow
    /// along the bottom+right half (split at the top-right and bottom-left corners),
    /// giving the tile a raised look. GDI+ angles: 0°=E, 90°=S, 180°=W, 270°=N.
    /// </summary>
    private static void DrawBevel(Graphics g, RectangleF r, float radius, float w)
    {
        float d = radius * 2;
        float inset = w / 2f;                 // keep the stroke inside the tile
        RectangleF ir = RectangleF.Inflate(r, -inset, -inset);
        float id = Math.Min(d, Math.Min(ir.Width, ir.Height));

        using var light = new GraphicsPath();
        light.AddArc(ir.X, ir.Bottom - id, id, id, 135, 45);     // upper part of bottom-left corner
        light.AddArc(ir.X, ir.Y, id, id, 180, 90);               // top-left corner
        light.AddArc(ir.Right - id, ir.Y, id, id, 270, 45);      // left part of top-right corner

        using var dark = new GraphicsPath();
        dark.AddArc(ir.Right - id, ir.Y, id, id, 315, 45);       // right part of top-right corner
        dark.AddArc(ir.Right - id, ir.Bottom - id, id, id, 0, 90); // bottom-right corner
        dark.AddArc(ir.X, ir.Bottom - id, id, id, 90, 45);       // lower part of bottom-left corner

        using (var pen = new Pen(BevelLight, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawPath(pen, light);
        using (var pen = new Pen(BevelDark, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            g.DrawPath(pen, dark);
    }
}
