using WifiHeat.Core.Geometry;
using WifiHeat.Core.Propagation;

namespace WifiHeat.Core.Simulation;

/// <summary>RSSI estimado en un punto: L_0 más los muros cruzados por el trayecto directo AP–punto.</summary>
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
        foreach (ResolvedWall rw in scene.Walls)
        {
            if (rw.Wall.IsCrossedBy(path))
            {
                double loss = rw.Material.LossDb(settings.Band);
                crossings.Add(new WallCrossing(rw.Wall.Id, rw.Material.Id, rw.Material.Name, loss));
                wallLoss += loss;
            }
        }

        double rx = LinkBudget.ReceivedPowerDbm(settings, pathLoss, wallLoss);
        return new PointCalculation(point, ap, settings, distance, effective, pathLoss, crossings, wallLoss, rx);
    }

    /// <summary>Versión rápida para la grilla: solo el valor en dBm.</summary>
    public static double ReceivedPowerDbm(SimulationScene scene, AccessPoint ap, RadioSettings settings, Vec2 point)
    {
        var path = new Segment2(ap.Position, point);
        double pathLoss = PathLoss.FreeSpaceDbFromMetres(settings.FrequencyMHz, path.Length);
        double wallLoss = 0;
        foreach (ResolvedWall rw in scene.Walls)
        {
            if (rw.Wall.IsCrossedBy(path))
            {
                wallLoss += rw.Material.LossDb(settings.Band);
            }
        }

        return LinkBudget.ReceivedPowerDbm(settings, pathLoss, wallLoss);
    }
}
