namespace WifiHeat.Core.Simulation;

/// <summary>Valores de RSSI estimado (dBm) por celda, indexados [columna, fila].</summary>
public sealed class RssiGrid(GridSpec spec, double[,] values)
{
    public GridSpec Spec { get; } = spec;

    public double this[int column, int row] => values[column, row];

    public double Min => values.Cast<double>().Min();

    public double Max => values.Cast<double>().Max();
}
