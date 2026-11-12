using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;

namespace WifiHeat.Core.Simulation;

/// <summary>Muros ya validados con su material resuelto, listos para calcular.</summary>
public sealed class SimulationScene
{
    public SimulationScene(IReadOnlyList<Wall> walls, MaterialCatalog catalog)
    {
        var resolved = new List<ResolvedWall>(walls.Count);
        foreach (Wall wall in walls)
        {
            if (wall.MaterialId is null || !catalog.TryGet(wall.MaterialId, out Material material))
            {
                throw new InvalidOperationException($"El muro '{wall.Id}' no tiene un material válido. Validar antes de calcular.");
            }

            resolved.Add(new ResolvedWall(wall, material));
        }

        Walls = resolved;
    }

    public IReadOnlyList<ResolvedWall> Walls { get; }

    public BoundingBox Bounds() => BoundingBox.FromPoints(Walls.SelectMany(w => w.Wall.Segments).SelectMany(s => new[] { s.A, s.B }));
}

public sealed record ResolvedWall(Wall Wall, Material Material);
