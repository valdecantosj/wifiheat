using WifiHeat.Core.Geometry;
using WifiHeat.Core.Propagation;

namespace WifiHeat.Core.Simulation;

/// <summary>
/// Muro o columna contado en el trayecto. JunctionWith: obstáculos tocados en el mismo punto, cuya pérdida no se suma.
/// En una columna, LossDb incluye ReinforcementLossDb, el adicional de su armadura.
/// </summary>
public sealed record WallCrossing(
    string WallId,
    string MaterialId,
    string MaterialName,
    double LossDb,
    IReadOnlyList<string> JunctionWith,
    Reinforcement? Reinforcement = null,
    double ReinforcementLossDb = 0)
{
    public bool IsColumn => Reinforcement is not null;
}

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
