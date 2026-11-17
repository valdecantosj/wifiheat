namespace WifiHeat.Core.Geometry;

/// <summary>Punto o vector en el plano, en metros.</summary>
public readonly record struct Vec2(double X, double Y)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator *(Vec2 a, double k) => new(a.X * k, a.Y * k);

    public double Length => Math.Sqrt(X * X + Y * Y);

    public static double Distance(Vec2 a, Vec2 b) => (a - b).Length;

    /// <summary>Producto vectorial 2D (componente z).</summary>
    public static double Cross(Vec2 a, Vec2 b) => a.X * b.Y - a.Y * b.X;

    /// <summary>Producto escalar.</summary>
    public static double Dot(Vec2 a, Vec2 b) => a.X * b.X + a.Y * b.Y;

    public override string ToString() => FormattableString.Invariant($"({X:0.###}; {Y:0.###})");
}
