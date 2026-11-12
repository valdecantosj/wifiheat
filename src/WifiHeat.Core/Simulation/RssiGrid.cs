using WifiHeat.Core.Geometry;

namespace WifiHeat.Core.Simulation;

/// <summary>
/// Valores de RSSI estimado (dBm) por celda, indexados [columna, fila].
/// Las celdas que no tocan el área de cobertura valen NaN (sin valor).
/// </summary>
public sealed class RssiGrid
{
    private readonly double[,] _values;

    public RssiGrid(GridSpec spec, double[,] values, Polygon2? area = null)
    {
        Spec = spec;
        _values = values;
        Area = area ?? Polygon2.Rectangle(new BoundingBox(spec.Origin, new Vec2(spec.Origin.X + spec.WidthM, spec.Origin.Y + spec.HeightM)));
    }

    public GridSpec Spec { get; }

    public Polygon2 Area { get; }

    public double this[int column, int row] => _values[column, row];

    public bool HasValue(int column, int row) => !double.IsNaN(_values[column, row]);

    public int ValueCount => _values.Cast<double>().Count(v => !double.IsNaN(v));

    public double Min => _values.Cast<double>().Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Min();

    public double Max => _values.Cast<double>().Where(v => !double.IsNaN(v)).DefaultIfEmpty(double.NaN).Max();
}
