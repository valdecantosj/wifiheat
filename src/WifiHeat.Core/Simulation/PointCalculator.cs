using WifiHeat.Core.Geometry;
using WifiHeat.Core.Propagation;

namespace WifiHeat.Core.Simulation;

/// <summary>RSSI estimado en un punto: L_0 más los muros cruzados por el trayecto directo AP–punto (regla de <see cref="PathCrossings"/>).</summary>
public static class PointCalculator
{
    public static PointCalculation Calculate(SimulationScene scene, AccessPoint ap, RadioSettings settings, Vec2 point)
    {
        var path = new Segment2(ap.Position, point);
        double distance = path.Length;
        double effective = PathLoss.EffectiveDistanceM(distance);
        double pathLoss = PathLoss.FreeSpaceDbFromMetres(settings.FrequencyMHz, distance);

        var crossings = new List<WallCrossing>();
        double wallLoss = 0;
        foreach (CrossingGroup group in PathCrossings.Find(scene, path, settings.Band))
        {
            ResolvedWall rw = group.Counted;
            crossings.Add(new WallCrossing(rw.Wall.Id, rw.Material.Id, rw.Material.Name, group.LossDb, [.. group.Others.Select(o => o.Wall.Id)],
                rw.Wall.IsColumn ? rw.Wall.Reinforcement : null, rw.AdditionalLossDb));
            wallLoss += group.LossDb;
        }

        double rx = LinkBudget.ReceivedPowerDbm(settings, pathLoss, wallLoss);
        return new PointCalculation(point, ap, settings, distance, effective, pathLoss, crossings, wallLoss, rx);
    }

    /// <summary>Con un modelo distinto de línea recta arma un <see cref="PathEngine"/> para esta consulta.</summary>
    public static PointCalculation Calculate(SimulationScene scene, AccessPoint ap, RadioSettings settings, Vec2 point,
        PropagationModel model, PropagationOptions? options = null) =>
        model == PropagationModel.StraightLine
            ? Calculate(scene, ap, settings, point)
            : Calculate(PathEngine.For(scene, ap, settings, model, options), point);

    /// <summary>Consulta con difracción: los datos de la recta AP–punto más los trayectos sumados y su total.</summary>
    public static PointCalculation Calculate(PathEngine engine, Vec2 point) =>
        Calculate(engine.Scene, engine.AccessPoint, engine.Settings, point) with
        {
            Model = engine.Model,
            Paths = engine.Paths(point),
            ReceivedPowerDbm = engine.ReceivedPowerDbm(point),
        };

    /// <summary>Versión rápida para la grilla: solo el valor en dBm.</summary>
    public static double ReceivedPowerDbm(SimulationScene scene, AccessPoint ap, RadioSettings settings, Vec2 point)
    {
        var path = new Segment2(ap.Position, point);
        double pathLoss = PathLoss.FreeSpaceDbFromMetres(settings.FrequencyMHz, path.Length);
        return LinkBudget.ReceivedPowerDbm(settings, pathLoss, PathCrossings.TotalLossDb(scene, path, settings.Band));
    }
}
