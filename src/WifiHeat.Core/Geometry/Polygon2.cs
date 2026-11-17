namespace WifiHeat.Core.Geometry;

/// <summary>Polígono simple cerrado, en metros. El último vértice se une con el primero.</summary>
public sealed class Polygon2
{
    private const double Tolerance = 1e-9;
    private const double ShrinkM = 1e-6;

    /// <summary>Distancia al borde por debajo de la cual un punto no se considera interior estricto.</summary>
    public const double StrictToleranceM = 1e-6;

    public Polygon2(IEnumerable<Vec2> vertices)
    {
        var list = new List<Vec2>();
        foreach (Vec2 v in vertices)
        {
            if (list.Count > 0 && Vec2.Distance(list[^1], v) <= Tolerance) continue;
            list.Add(v);
        }

        if (list.Count > 1 && Vec2.Distance(list[0], list[^1]) <= Tolerance) list.RemoveAt(list.Count - 1);
        if (list.Count < 3) throw new ArgumentException("El polígono necesita al menos tres vértices distintos.", nameof(vertices));

        Vertices = list;
        AreaM2 = Math.Abs(SignedArea(list));
        if (AreaM2 <= Tolerance) throw new ArgumentException("El polígono no encierra superficie.", nameof(vertices));
        Bounds = BoundingBox.FromPoints(list);
    }

    public IReadOnlyList<Vec2> Vertices { get; }

    public double AreaM2 { get; }

    public BoundingBox Bounds { get; }

    public static Polygon2 Rectangle(BoundingBox box) =>
        new([box.Min, new Vec2(box.Max.X, box.Min.Y), box.Max, new Vec2(box.Min.X, box.Max.Y)]);

    public IEnumerable<Segment2> Edges()
    {
        for (int i = 0; i < Vertices.Count; i++) yield return new Segment2(Vertices[i], Vertices[(i + 1) % Vertices.Count]);
    }

    /// <summary>Punto en polígono por regla par-impar (rayo horizontal). Los puntos del borde cuentan como interiores.</summary>
    public bool Contains(Vec2 p)
    {
        if (p.X < Bounds.Min.X - Tolerance || p.X > Bounds.Max.X + Tolerance ||
            p.Y < Bounds.Min.Y - Tolerance || p.Y > Bounds.Max.Y + Tolerance)
        {
            return false;
        }

        bool inside = false;
        for (int i = 0, j = Vertices.Count - 1; i < Vertices.Count; j = i++)
        {
            Vec2 a = Vertices[i], b = Vertices[j];
            if (SegmentIntersection.Intersects(new Segment2(a, b), new Segment2(p, p))) return true;
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }

        return inside;
    }

    /// <summary>Punto dentro del polígono y a más de <see cref="StrictToleranceM"/> de su borde.</summary>
    public bool ContainsStrictly(Vec2 p)
    {
        bool inside = false;
        for (int i = 0, j = Vertices.Count - 1; i < Vertices.Count; j = i++)
        {
            Vec2 a = Vertices[i], b = Vertices[j];
            Vec2 ab = b - a;
            double t = Math.Max(0.0, Math.Min(1.0, Vec2.Dot(p - a, ab) / Vec2.Dot(ab, ab)));
            if (Vec2.Distance(p, a + ab * t) <= StrictToleranceM) return false;
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X) inside = !inside;
        }

        return inside;
    }

    /// <summary>
    /// Tramos del segmento que pasan por el interior estricto del polígono, como fracciones (0 a 1) de su longitud.
    /// Correr a lo largo de un lado o tocar un vértice no es pasar por el interior.
    /// </summary>
    public IReadOnlyList<(double From, double To)> InteriorIntervals(Segment2 segment)
    {
        Vec2 r = segment.B - segment.A;
        double length = r.Length;
        if (length <= 0) return [];

        var cuts = new List<double> { 0.0, 1.0 };
        foreach (Segment2 edge in Edges())
        {
            if (!SegmentIntersection.Intersects(segment, edge)) continue;
            (double from, double to) = SegmentIntersection.ContactSpan(segment, edge);
            cuts.Add(from / length);
            cuts.Add(to / length);
        }

        cuts.Sort();

        List<(double From, double To)>? result = null;
        for (int i = 0; i + 1 < cuts.Count; i++)
        {
            double t0 = cuts[i], t1 = cuts[i + 1];
            if (t1 - t0 <= 1e-9) continue;
            if (ContainsStrictly(segment.A + r * ((t0 + t1) / 2))) (result ??= []).Add((t0, t1));
        }

        return result ?? (IReadOnlyList<(double From, double To)>)[];
    }

    /// <summary>¿El rectángulo y el polígono comparten superficie? Tocarse solo en un borde no cuenta.</summary>
    public bool Overlaps(BoundingBox box)
    {
        box = box.Expand(-ShrinkM);
        if (box.Max.X < Bounds.Min.X || box.Min.X > Bounds.Max.X || box.Max.Y < Bounds.Min.Y || box.Min.Y > Bounds.Max.Y) return false;

        Vec2[] corners = [box.Min, new Vec2(box.Max.X, box.Min.Y), box.Max, new Vec2(box.Min.X, box.Max.Y)];
        if (Contains(new Vec2((box.Min.X + box.Max.X) / 2, (box.Min.Y + box.Max.Y) / 2))) return true;
        foreach (Vec2 c in corners) if (Contains(c)) return true;
        foreach (Vec2 v in Vertices)
        {
            if (v.X >= box.Min.X && v.X <= box.Max.X && v.Y >= box.Min.Y && v.Y <= box.Max.Y) return true;
        }

        for (int i = 0; i < 4; i++)
        {
            var side = new Segment2(corners[i], corners[(i + 1) % 4]);
            foreach (Segment2 edge in Edges()) if (SegmentIntersection.Intersects(side, edge)) return true;
        }

        return false;
    }

    private static double SignedArea(List<Vec2> v)
    {
        double sum = 0;
        for (int i = 0; i < v.Count; i++) sum += Vec2.Cross(v[i], v[(i + 1) % v.Count]);
        return sum / 2;
    }
}
