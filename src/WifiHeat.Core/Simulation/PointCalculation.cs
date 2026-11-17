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

/// <summary>
/// Resultado explicable del cálculo en un punto.
/// DistanceM, PathLossDb, Walls y WallLossDb describen la recta AP–punto. ReceivedPowerDbm es el total del modelo:
/// con difracción, la suma en potencia de Paths (null en el modelo de línea recta).
/// </summary>
public sealed record PointCalculation(
    Vec2 Point,
    AccessPoint AccessPoint,
    RadioSettings Settings,
    double DistanceM,
    double EffectiveDistanceM,
    double PathLossDb,
    IReadOnlyList<WallCrossing> Walls,
    double WallLossDb,
    double ReceivedPowerDbm,
    PropagationModel Model = PropagationModel.StraightLine,
    IReadOnlyList<PathContribution>? Paths = null);
