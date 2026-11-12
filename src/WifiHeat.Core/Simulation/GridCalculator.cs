using WifiHeat.Core.Propagation;

namespace WifiHeat.Core.Simulation;

public static class GridCalculator
{
    /// <summary>Calcula todas las celdas. Comparación directa trayecto–todos los muros (sin índice espacial).</summary>
    public static RssiGrid Calculate(SimulationScene scene, AccessPoint ap, RadioSettings settings, GridSpec spec)
    {
        var values = new double[spec.Columns, spec.Rows];
        Parallel.For(0, spec.Columns, column =>
        {
            for (int row = 0; row < spec.Rows; row++)
            {
                values[column, row] = PointCalculator.ReceivedPowerDbm(scene, ap, settings, spec.CellCenter(column, row));
            }
        });

        return new RssiGrid(spec, values);
    }
}
