using WifiHeat.Core.Geometry;
using WifiHeat.Core.Propagation;

namespace WifiHeat.Core.Simulation;

public static class GridCalculator
{
    /// <summary>
    /// Calcula las celdas que tocan el área de cobertura; el resto queda en NaN.
    /// Comparación directa trayecto–todos los muros (sin índice espacial).
    /// </summary>
    public static RssiGrid Calculate(SimulationScene scene, AccessPoint ap, RadioSettings settings, GridSpec spec, Polygon2 area)
    {
        var values = new double[spec.Columns, spec.Rows];
        Parallel.For(0, spec.Columns, column =>
        {
            for (int row = 0; row < spec.Rows; row++)
            {
                Vec2 min = spec.CellMin(column, row);
                var cell = new BoundingBox(min, new Vec2(min.X + spec.CellSizeM, min.Y + spec.CellSizeM));
                values[column, row] = area.Overlaps(cell)
                    ? PointCalculator.ReceivedPowerDbm(scene, ap, settings, spec.CellCenter(column, row))
                    : double.NaN;
            }
        });

        return new RssiGrid(spec, values, area);
    }

    /// <summary>Con línea recta es el cálculo de arriba; con otro modelo arma un <see cref="PathEngine"/> y lo consulta en cada celda.</summary>
    public static RssiGrid Calculate(SimulationScene scene, AccessPoint ap, RadioSettings settings, GridSpec spec, Polygon2 area,
        PropagationModel model, PropagationOptions? options = null) =>
        model == PropagationModel.StraightLine
            ? Calculate(scene, ap, settings, spec, area)
            : Calculate(PathEngine.For(scene, ap, settings, model, options), spec, area);

    public static RssiGrid Calculate(PathEngine engine, GridSpec spec, Polygon2 area)
    {
        var values = new double[spec.Columns, spec.Rows];
        Parallel.For(0, spec.Columns, column =>
        {
            for (int row = 0; row < spec.Rows; row++)
            {
                Vec2 min = spec.CellMin(column, row);
                var cell = new BoundingBox(min, new Vec2(min.X + spec.CellSizeM, min.Y + spec.CellSizeM));
                values[column, row] = area.Overlaps(cell) ? engine.ReceivedPowerDbm(spec.CellCenter(column, row)) : double.NaN;
            }
        });

        return new RssiGrid(spec, values, area);
    }
}
