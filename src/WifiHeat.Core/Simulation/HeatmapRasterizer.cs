using WifiHeat.Core.Geometry;

namespace WifiHeat.Core.Simulation;

/// <summary>
/// Imagen del mapa de calor en memoria: 4 bytes por píxel en orden B, G, R, A; la fila 0 es la de arriba.
/// Origin es la esquina inferior izquierda, en metros.
/// </summary>
public sealed record HeatmapImage(Vec2 Origin, double PixelSizeM, int Width, int Height, byte[] Bgra)
{
    public double WidthM => Width * PixelSizeM;

    public double HeightM => Height * PixelSizeM;

    public (byte R, byte G, byte B, byte A) Pixel(int x, int y)
    {
        int i = (y * Width + x) * 4;
        return (Bgra[i + 2], Bgra[i + 1], Bgra[i], Bgra[i + 3]);
    }
}

/// <summary>Convierte la grilla en una imagen continua: interpolación bilineal, rampa de color y recorte al área.</summary>
public static class HeatmapRasterizer
{
    /// <summary>Dentro del área el píxel es opaco; la atenuación la aplica quien muestra la imagen (ver <see cref="OverlayFade"/>).</summary>
    public const byte OpaqueAlpha = 255;

    /// <summary>Lado mayor deseado en píxeles, para que el degradé se vea continuo en pantalla.</summary>
    public const int TargetLongSidePx = 1600;

    public const int MaxPixels = 16_000_000;

    /// <summary>Tamaño de píxel: como máximo un cuarto de la celda, más fino si el área es chica; acotado por <see cref="MaxPixels"/>.</summary>
    public static double PixelSizeFor(GridSpec spec, BoundingBox extent)
    {
        double size = Math.Min(spec.CellSizeM / 4, Math.Max(extent.Width, extent.Height) / TargetLongSidePx);
        double pixels = extent.Width / size * (extent.Height / size);
        return pixels > MaxPixels ? Math.Sqrt(extent.Width * extent.Height / MaxPixels) : size;
    }

    public static HeatmapImage Render(RssiGrid grid, byte alpha = OpaqueAlpha) =>
        Render(grid, PixelSizeFor(grid.Spec, grid.Area.Bounds), alpha);

    public static HeatmapImage Render(RssiGrid grid, double pixelSizeM, byte alpha)
    {
        if (pixelSizeM <= 0) throw new ArgumentOutOfRangeException(nameof(pixelSizeM));

        BoundingBox extent = grid.Area.Bounds;
        int width = Math.Max(1, (int)Math.Ceiling(extent.Width / pixelSizeM - 1e-9));
        int height = Math.Max(1, (int)Math.Ceiling(extent.Height / pixelSizeM - 1e-9));
        var bgra = new byte[width * height * 4];

        Parallel.For(0, height, y =>
        {
            double yM = extent.Min.Y + (height - y - 0.5) * pixelSizeM;
            for (int x = 0; x < width; x++)
            {
                var p = new Vec2(extent.Min.X + (x + 0.5) * pixelSizeM, yM);
                if (!grid.Area.Contains(p)) continue;

                double value = Sample(grid, p);
                if (double.IsNaN(value)) continue;

                Rgb color = SignalScale.RampColor(value);
                int i = (y * width + x) * 4;
                bgra[i] = color.B;
                bgra[i + 1] = color.G;
                bgra[i + 2] = color.R;
                bgra[i + 3] = alpha;
            }
        });

        return new HeatmapImage(extent.Min, pixelSizeM, width, height, bgra);
    }

    /// <summary>
    /// Interpolación bilineal entre centros de celda. Las celdas sin valor se ignoran y los pesos se renormalizan;
    /// fuera de la grilla se toma el borde. NaN si ninguna de las cuatro celdas vecinas tiene valor.
    /// </summary>
    public static double Sample(RssiGrid grid, Vec2 p)
    {
        GridSpec spec = grid.Spec;
        double fx = Math.Clamp((p.X - spec.Origin.X) / spec.CellSizeM - 0.5, 0, spec.Columns - 1);
        double fy = Math.Clamp((p.Y - spec.Origin.Y) / spec.CellSizeM - 0.5, 0, spec.Rows - 1);
        int c0 = (int)Math.Floor(fx), r0 = (int)Math.Floor(fy);
        int c1 = Math.Min(c0 + 1, spec.Columns - 1), r1 = Math.Min(r0 + 1, spec.Rows - 1);
        double tx = fx - c0, ty = fy - r0;

        double sum = 0, weights = 0, plain = 0;
        int valid = 0;
        Add(c0, r0, (1 - tx) * (1 - ty));
        Add(c1, r0, tx * (1 - ty));
        Add(c0, r1, (1 - tx) * ty);
        Add(c1, r1, tx * ty);

        if (valid == 0) return double.NaN;
        return weights > 1e-12 ? sum / weights : plain / valid;

        void Add(int c, int r, double w)
        {
            if (!grid.HasValue(c, r)) return;
            double v = grid[c, r];
            sum += v * w;
            weights += w;
            plain += v;
            valid++;
        }
    }
}
