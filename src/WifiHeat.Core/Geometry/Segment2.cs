namespace WifiHeat.Core.Geometry;

/// <summary>Segmento recto entre dos puntos, en metros.</summary>
public readonly record struct Segment2(Vec2 A, Vec2 B)
{
    public double Length => Vec2.Distance(A, B);
}
