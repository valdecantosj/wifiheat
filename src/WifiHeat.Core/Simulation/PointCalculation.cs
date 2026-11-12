using WifiHeat.Core.Geometry;
using WifiHeat.Core.Propagation;

namespace WifiHeat.Core.Simulation;

public sealed record WallCrossing(string WallId, string MaterialId, string MaterialName, double LossDb);

/// <summary>Resultado explicable del cálculo en un punto.</summary>
public sealed record PointCalculation(
    Vec2 Point,
    AccessPoint AccessPoint,
    RadioSettings Settings,
    double DistanceM,
    double EffectiveDistanceM,
    double PathLossDb,
    IReadOnlyList<WallCrossing> Walls,
    double WallLossDb,
    double ReceivedPowerDbm);
