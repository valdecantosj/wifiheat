namespace WifiHeat.Core.Geometry;

/// <summary>
/// Intersección entre el trayecto AP–punto y un segmento de muro.
/// Dos segmentos se cruzan si comparten al menos un punto.
/// Eso incluye cruces propios, toques en un extremo o vértice y superposición colineal.
/// El conteo por muro y por punto de contacto está en PathCrossings.
/// </summary>
public static class SegmentIntersection
{
    /// <summary>Tolerancia absoluta en m² para el producto vectorial (geometría en metros).</summary>
    public const double Epsilon = 1e-9;

    public static bool Intersects(Segment2 p, Segment2 q)
    {
        double d1 = Orientation(q.A, q.B, p.A);
        double d2 = Orientation(q.A, q.B, p.B);
        double d3 = Orientation(p.A, p.B, q.A);
        double d4 = Orientation(p.A, p.B, q.B);

        if (OppositeSides(d1, d2) && OppositeSides(d3, d4))
        {
            return true;
        }

        return (IsZero(d1) && OnSegment(q.A, q.B, p.A))
            || (IsZero(d2) && OnSegment(q.A, q.B, p.B))
            || (IsZero(d3) && OnSegment(p.A, p.B, q.A))
            || (IsZero(d4) && OnSegment(p.A, p.B, q.B));
    }

    /// <summary>
    /// Tramo del trayecto (distancias desde su inicio, en m) que toca el segmento: un punto o, si son colineales, un intervalo.
    /// Supone que <see cref="Intersects"/> ya dio verdadero.
    /// </summary>
    public static (double From, double To) ContactSpan(Segment2 path, Segment2 segment)
    {
        Vec2 r = path.B - path.A;
        double length = r.Length;
        if (length <= 0) return (0, 0);

        Vec2 s = segment.B - segment.A;
        double denominator = Vec2.Cross(r, s);
        if (Math.Abs(denominator) > 1e-12 * length * Math.Max(s.Length, 1e-12))
        {
            double t = Math.Clamp(Vec2.Cross(segment.A - path.A, s) / denominator, 0, 1);
            return (t * length, t * length);
        }

        double ta = Vec2.Dot(segment.A - path.A, r) / length;
        double tb = Vec2.Dot(segment.B - path.A, r) / length;
        return (Math.Clamp(Math.Min(ta, tb), 0, length), Math.Clamp(Math.Max(ta, tb), 0, length));
    }

    private static double Orientation(Vec2 a, Vec2 b, Vec2 c) => Vec2.Cross(b - a, c - a);

    private static bool IsZero(double value) => Math.Abs(value) <= Epsilon;

    private static bool OppositeSides(double a, double b) =>
        (a > Epsilon && b < -Epsilon) || (a < -Epsilon && b > Epsilon);

    /// <summary>c es colineal con a–b: ¿está dentro del rectángulo que encierra a–b?</summary>
    private static bool OnSegment(Vec2 a, Vec2 b, Vec2 c) =>
        c.X >= Math.Min(a.X, b.X) - Epsilon && c.X <= Math.Max(a.X, b.X) + Epsilon &&
        c.Y >= Math.Min(a.Y, b.Y) - Epsilon && c.Y <= Math.Max(a.Y, b.Y) + Epsilon;
}
