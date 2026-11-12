namespace WifiHeat.Core.Geometry;

/// <summary>Rectángulo alineado a los ejes, en metros.</summary>
public readonly record struct BoundingBox(Vec2 Min, Vec2 Max)
{
    public double Width => Max.X - Min.X;
    public double Height => Max.Y - Min.Y;

    public static BoundingBox FromPoints(IEnumerable<Vec2> points)
    {
        double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
        bool any = false;
        foreach (Vec2 p in points)
        {
            any = true;
            minX = Math.Min(minX, p.X); minY = Math.Min(minY, p.Y);
            maxX = Math.Max(maxX, p.X); maxY = Math.Max(maxY, p.Y);
        }

        if (!any)
        {
            throw new ArgumentException("Se necesita al menos un punto para calcular la envolvente.", nameof(points));
        }

        return new BoundingBox(new Vec2(minX, minY), new Vec2(maxX, maxY));
    }

    public BoundingBox Expand(double margin) =>
        new(new Vec2(Min.X - margin, Min.Y - margin), new Vec2(Max.X + margin, Max.Y + margin));
}
