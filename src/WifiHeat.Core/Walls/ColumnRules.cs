using WifiHeat.Core.Geometry;

namespace WifiHeat.Core.Walls;

/// <summary>Reglas fijas para reconocer columnas entre las figuras cerradas del plano.</summary>
public static class ColumnRules
{
    /// <summary>Lado máximo de una figura para tomarla como columna.</summary>
    public const double MaxSideM = 1.5;

    /// <summary>Dos figuras cuyos contornos difieren menos que esto son la misma columna (un hatch y su contorno).</summary>
    public const double SameFootprintToleranceM = 0.02;

    private const double Slack = 1e-9;

    /// <summary>Ningún lado supera <see cref="MaxSideM"/> ni la figura es más ancha que la diagonal de un cuadrado de ese lado.</summary>
    public static bool IsSmall(Polygon2 figure)
    {
        if (figure.Edges().Any(e => e.Length > MaxSideM + Slack)) return false;

        double limit = MaxSideM * Math.Sqrt(2) + Slack;
        IReadOnlyList<Vec2> v = figure.Vertices;
        for (int i = 0; i < v.Count; i++)
        {
            for (int j = i + 1; j < v.Count; j++)
            {
                if (Vec2.Distance(v[i], v[j]) > limit) return false;
            }
        }

        return true;
    }

    /// <summary>Contorno cerrado y pequeño formado por los tramos de una entidad, o null.</summary>
    public static Polygon2? SmallClosedFigure(IReadOnlyList<Segment2> segments, bool closed)
    {
        if (!closed || segments.Count < 3) return null;

        try
        {
            var figure = new Polygon2(segments.Select(s => s.A));
            return IsSmall(figure) ? figure : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public static bool SameFootprint(Polygon2 a, Polygon2 b)
    {
        double t = SameFootprintToleranceM;
        if (Math.Abs(a.Bounds.Min.X - b.Bounds.Min.X) > t || Math.Abs(a.Bounds.Min.Y - b.Bounds.Min.Y) > t ||
            Math.Abs(a.Bounds.Max.X - b.Bounds.Max.X) > t || Math.Abs(a.Bounds.Max.Y - b.Bounds.Max.Y) > t)
        {
            return false;
        }

        double perimeter = Math.Max(a.Edges().Sum(e => e.Length), b.Edges().Sum(e => e.Length));
        return Math.Abs(a.AreaM2 - b.AreaM2) <= t * perimeter;
    }
}
