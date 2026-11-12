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

    /// <param name="heatmap">La misma imagen que se inserta en el dibujo.</param>
    public static void Save(string path, IReadOnlyList<Wall> walls, HeatmapImage heatmap, AccessPoint ap, Legend legend, int fadePercent)
    {
        var heatExtent = new BoundingBox(heatmap.Origin, new Vec2(heatmap.Origin.X + heatmap.WidthM, heatmap.Origin.Y + heatmap.HeightM));
        BoundingBox extent = BoundingBox.FromPoints(
            walls.SelectMany(w => w.Segments).SelectMany(s => new[] { s.A, s.B })
                .Concat([heatExtent.Min, heatExtent.Max, ap.Position]));
        double widthM = Math.Max(extent.Width, 1e-3), heightM = Math.Max(extent.Height, 1e-3);
        double scale = Math.Min(MaxMapWidthPx / widthM, MaxMapHeightPx / heightM);
        int mapWidth = Math.Max(1, (int)Math.Ceiling(widthM * scale));
        int mapHeight = Math.Max(1, (int)Math.Ceiling(heightM * scale));
        int legendHeight = 60 + legend.RowCount * 34 + 50;
        int width = MarginPx + mapWidth + MarginPx + LegendWidthPx;
        int height = Math.Max(mapHeight, legendHeight) + 2 * MarginPx;

        using var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Color.White);

        float X(double xM) => (float)(MarginPx + (xM - extent.Min.X) * scale);
        float Y(double yM) => (float)(MarginPx + mapHeight - (yM - extent.Min.Y) * scale);

        using (Bitmap heat = HeatmapBitmap.ToBitmap(heatmap))
        using (var attributes = new ImageAttributes())
        {
            attributes.SetWrapMode(WrapMode.TileFlipXY);
            attributes.SetColorMatrix(new ColorMatrix { Matrix33 = OverlayFade.Alpha(fadePercent) / 255f });
            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            float left = X(heatExtent.Min.X), top = Y(heatExtent.Max.Y);
            var target = Rectangle.Round(new RectangleF(left, top, X(heatExtent.Max.X) - left, Y(heatExtent.Min.Y) - top));
            g.DrawImage(heat, target, 0, 0, heat.Width, heat.Height, GraphicsUnit.Pixel, attributes);
            g.PixelOffsetMode = PixelOffsetMode.Default;
        }

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
            using var swatch = new SolidBrush(Color.FromArgb(OverlayFade.Alpha(fadePercent), level.R, level.G, level.B));
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
