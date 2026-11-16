using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;

namespace WifiHeat.Core.Simulation;

/// <summary>Muros y columnas ya validados con su material resuelto, listos para calcular.</summary>
public sealed class SimulationScene
{
    /// <param name="includeReinforcement">False calcula las columnas como hormigón sin armadura.</param>
    public SimulationScene(IReadOnlyList<Wall> walls, MaterialCatalog catalog, bool includeReinforcement = true)
    {
        var resolved = new List<ResolvedWall>(walls.Count);
        foreach (Wall wall in walls)
        {
            if (wall.MaterialId is null || !catalog.TryGet(wall.MaterialId, out Material material))
            {
                throw new InvalidOperationException($"El muro '{wall.Id}' no tiene un material válido. Validar antes de calcular.");
            }

            if (!wall.IsColumn)
            {
                resolved.Add(new ResolvedWall(wall, material));
                continue;
            }

            if (wall.Reinforcement is not { } level || wall.Footprint() is not { } footprint)
            {
                throw new InvalidOperationException($"La columna '{wall.Id}' no tiene armadura o no es una figura cerrada. Validar antes de calcular.");
            }

            resolved.Add(new ResolvedWall(wall, material, includeReinforcement ? catalog.Columns.AdditionalLossDb(level) : 0, footprint));
        }

        Walls = resolved;
    }

    private SimulationScene(IReadOnlyList<ResolvedWall> walls) => Walls = walls;

    public IReadOnlyList<ResolvedWall> Walls { get; }

    public bool HasColumns => Walls.Any(w => w.Wall.IsColumn);

    /// <summary>True si alguna columna suma un adicional de armadura mayor que cero.</summary>
    public bool HasReinforcement => Walls.Any(w => w.AdditionalLossDb > 0);

    /// <summary>Cantidad de columnas que suman un adicional de armadura.</summary>
    public int ReinforcedColumnCount => Walls.Count(w => w.AdditionalLossDb > 0);

    /// <summary>La misma escena con las columnas como hormigón sin armadura.</summary>
    public SimulationScene WithoutReinforcement() => new([.. Walls.Select(w => w with { AdditionalLossDb = 0 })]);

    public BoundingBox Bounds() => BoundingBox.FromPoints(Walls.SelectMany(w => w.Wall.Segments).SelectMany(s => new[] { s.A, s.B }));

    /// <summary>Columna que contiene el punto (el borde cuenta como interior), o null.</summary>
    public ResolvedWall? ColumnContaining(Vec2 point) => Walls.FirstOrDefault(w => w.Footprint is not null && w.Footprint.Contains(point));
}

/// <summary>Obstáculo con su material. AdditionalLossDb: adicional de armadura de una columna. Footprint: contorno de la columna.</summary>
public sealed record ResolvedWall(Wall Wall, Material Material, double AdditionalLossDb = 0, Polygon2? Footprint = null)
{
    public double LossDb(Band band) => Material.LossDb(band) + AdditionalLossDb;
}
