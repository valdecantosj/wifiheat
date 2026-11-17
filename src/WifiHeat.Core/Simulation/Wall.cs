using WifiHeat.Core.Geometry;

namespace WifiHeat.Core.Simulation;

public enum ObstacleKind
{
    Wall,
    Column,
}

/// <summary>Nivel de armadura de una columna de hormigón armado.</summary>
public enum Reinforcement
{
    Low = 1,
    Medium = 2,
    High = 3,
}

/// <summary>
/// Obstáculo de simulación: un muro (eje de uno o más segmentos) o una columna (contorno cerrado), en metros.
/// Material y espesor pueden faltar al leerse del DWG; la validación lo informa como FALTA PROPIEDAD.
/// El espesor se registra pero no modifica la pérdida. La columna suma a su material el adicional de su armadura.
/// El conteo de cruces está en <see cref="PathCrossings"/>: cada obstáculo cuenta a lo sumo una vez por trayecto.
/// </summary>
public sealed record Wall(
    string Id,
    IReadOnlyList<Segment2> Segments,
    string? MaterialId,
    double? ThicknessM,
    ObstacleKind Kind = ObstacleKind.Wall,
    Reinforcement? Reinforcement = null)
{
    private const double ClosedToleranceM = 1e-6;

    public bool IsColumn => Kind == ObstacleKind.Column;

    public static Wall Column(string id, Polygon2 footprint, string? materialId, Reinforcement? reinforcement) =>
        new(id, [.. footprint.Edges()], materialId, null, ObstacleKind.Column, reinforcement);

    /// <summary>Contorno cerrado que forman los segmentos, o null si no encierran superficie.</summary>
    public Polygon2? Footprint()
    {
        if (Segments.Count < 3) return null;
        for (int i = 0; i < Segments.Count; i++)
        {
            if (Vec2.Distance(Segments[i].B, Segments[(i + 1) % Segments.Count].A) > ClosedToleranceM) return null;
        }

        try
        {
            return new Polygon2(Segments.Select(s => s.A));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
