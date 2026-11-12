using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.Export;

/// <summary>Compone la imagen del resultado con GDI+: mapa de calor, muros, AP y leyenda.</summary>
public static class PngComposer
{
    private const int MaxMapWidthPx = 1400;
    private const int MaxMapHeightPx = 1000;
    private const int MarginPx = 40;
    private const int LegendWidthPx = 330;
    private const int HeatAlpha = 185;

    public static void Save(string path, IReadOnlyList<Wall> walls, RssiGrid grid, AccessPoint ap, Legend legend)
    {
        GridSpec spec = grid.Spec;
        double scale = Math.Min(MaxMapWidthPx / spec.WidthM, MaxMapHeightPx / spec.HeightM);
        int mapWidth = Math.Max(1, (int)Math.Ceiling(spec.WidthM * scale));
        int mapHeight = Math.Max(1, (int)Math.Ceiling(spec.HeightM * scale));
        int legendHeight = 60 + legend.RowCount * 34 + 50;
        int width = MarginPx + mapWidth + MarginPx + LegendWidthPx;
        int height = Math.Max(mapHeight, legendHeight) + 2 * MarginPx;

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);

        float X(double xM) => (float)(MarginPx + (xM - spec.Origin.X) * scale);
        float Y(double yM) => (float)(MarginPx + mapHeight - (yM - spec.Origin.Y) * scale);

        SolidBrush[] brushes = [.. SignalScale.Levels.Select(l => new SolidBrush(Color.FromArgb(HeatAlpha, l.R, l.G, l.B)))];
        foreach (ColorRun run in ColorRuns.FromGrid(grid))
        {
            double x0 = spec.Origin.X + run.StartColumn * spec.CellSizeM;
            double y1 = spec.Origin.Y + (run.Row + 1) * spec.CellSizeM;
            float left = X(x0), top = Y(y1);
            float right = X(x0 + run.Length * spec.CellSizeM), bottom = Y(y1 - spec.CellSizeM);
            g.FillRectangle(brushes[run.LevelIndex], left, top, right - left, bottom - top);
        }

        foreach (SolidBrush brush in brushes) brush.Dispose();

        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAlias;

        using (var wallPen = new Pen(Color.Black, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            foreach (Segment2 segment in walls.SelectMany(w => w.Segments))
            {
                g.DrawLine(wallPen, X(segment.A.X), Y(segment.A.Y), X(segment.B.X), Y(segment.B.Y));
            }
        }

        using var labelFont = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var textFont = new Font("Segoe UI", 11f);
        using var titleFont = new Font("Segoe UI", 15f, FontStyle.Bold);
        using var smallFont = new Font("Segoe UI", 9f);

        float apX = X(ap.Position.X), apY = Y(ap.Position.Y);
        using (var apBrush = new SolidBrush(Color.FromArgb(0, 70, 200)))
        using (var apPen = new Pen(Color.White, 2f))
        {
            g.FillEllipse(apBrush, apX - 7, apY - 7, 14, 14);
            g.DrawEllipse(apPen, apX - 7, apY - 7, 14, 14);
            g.DrawString(ap.Name, labelFont, apBrush, apX + 9, apY - 22);
        }

        float lx = MarginPx + mapWidth + MarginPx;
        float ly = MarginPx;
        g.DrawString(legend.Title, titleFont, Brushes.Black, lx, ly);
        ly += 36;
        foreach (string detail in legend.Details)
        {
            g.DrawString(detail, textFont, Brushes.Black, lx, ly);
            ly += 24;
        }

        ly += 12;
        foreach (SignalLevel level in legend.Levels)
        {
            using var swatch = new SolidBrush(Color.FromArgb(HeatAlpha, level.R, level.G, level.B));
            g.FillRectangle(swatch, lx, ly, 40, 24);
            g.DrawRectangle(Pens.Gray, lx, ly, 40, 24);
            g.DrawString(level.Label, textFont, Brushes.Black, lx + 52, ly + 2);
            ly += 34;
        }

        ly += 10;
        g.DrawString("Estimación del modelo; no es una medición.", smallFont, Brushes.DimGray, lx, ly);

        string? folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
        bitmap.Save(path, ImageFormat.Png);
    }
}
