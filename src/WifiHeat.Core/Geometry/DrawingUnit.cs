namespace WifiHeat.Core.Geometry;

/// <summary>Unidad lineal del dibujo de origen. Core trabaja siempre en metros.</summary>
public enum DrawingUnit
{
    Metres,
    Centimetres,
    Millimetres,
}

public static class DrawingUnits
{
    public static double MetresPerUnit(DrawingUnit unit) => unit switch
    {
        DrawingUnit.Metres => 1.0,
        DrawingUnit.Centimetres => 0.01,
        DrawingUnit.Millimetres => 0.001,
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    public static double ToMetres(DrawingUnit unit, double value) => value * MetresPerUnit(unit);

    public static double FromMetres(DrawingUnit unit, double metres) => metres / MetresPerUnit(unit);

    public static Vec2 ToMetres(DrawingUnit unit, double x, double y) => new(ToMetres(unit, x), ToMetres(unit, y));

    /// <summary>Código corto con el que se guarda la unidad en el dibujo.</summary>
    public static string Code(DrawingUnit unit) => unit switch
    {
        DrawingUnit.Metres => "m",
        DrawingUnit.Centimetres => "cm",
        DrawingUnit.Millimetres => "mm",
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };

    public static bool TryParseCode(string? code, out DrawingUnit unit)
    {
        switch (code?.Trim().ToLowerInvariant())
        {
            case "m": unit = DrawingUnit.Metres; return true;
            case "cm": unit = DrawingUnit.Centimetres; return true;
            case "mm": unit = DrawingUnit.Millimetres; return true;
            default: unit = default; return false;
        }
    }

    public static string Label(DrawingUnit unit) => unit switch
    {
        DrawingUnit.Metres => "metros",
        DrawingUnit.Centimetres => "centímetros",
        DrawingUnit.Millimetres => "milímetros",
        _ => throw new ArgumentOutOfRangeException(nameof(unit)),
    };
}
