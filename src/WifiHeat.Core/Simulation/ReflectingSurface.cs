using WifiHeat.Core.Geometry;
using WifiHeat.Core.Propagation;

namespace WifiHeat.Core.Simulation;

/// <summary>
/// Segmento de muro o cara de columna sobre el que rebota un trayecto, en el modelo con reflexiones.
/// Normal: unitaria, a la izquierda de A → B. OutwardOnly: en una cara de columna, la normal apunta hacia afuera y solo se
/// refleja de ese lado; un muro refleja de los dos lados.
/// </summary>
public sealed record ReflectingSurface(string WallId, bool IsColumn, Segment2 Segment, Vec2 Normal, bool OutwardOnly, Dielectric Material)
{
    /// <summary>Distancia con signo (m) del punto a la recta del segmento, positiva del lado de la normal.</summary>
    public double SignedDistance(Vec2 p) => Vec2.Dot(p - Segment.A, Normal);

    /// <summary>Simétrico del punto respecto de la recta del segmento.</summary>
    public Vec2 Mirror(Vec2 p) => p - Normal * (2 * SignedDistance(p));
}

public static class ReflectingSurfaces
{
    /// <summary>Segmentos más cortos que esto no reflejan (m).</summary>
    public const double MinLengthM = 1e-3;

    private const double OutsideProbeM = 1e-4;

    /// <summary>Segmentos de muros y caras de columnas cuyo material tiene propiedades eléctricas, en el orden de la escena.</summary>
    public static IReadOnlyList<ReflectingSurface> Find(SimulationScene scene)
    {
        var surfaces = new List<ReflectingSurface>();
        foreach (ResolvedWall rw in scene.Walls)
        {
            if (rw.Material.Reflection is not { } material) continue;

            foreach (Segment2 segment in rw.Wall.Segments)
            {
                double length = segment.Length;
                if (length < MinLengthM) continue;

                Vec2 along = (segment.B - segment.A) * (1 / length);
                var normal = new Vec2(-along.Y, along.X);
                if (rw.Footprint is { } footprint)
                {
                    Vec2 middle = (segment.A + segment.B) * 0.5;
                    if (footprint.Contains(middle + normal * OutsideProbeM)) normal = normal * -1;
                }

                surfaces.Add(new ReflectingSurface(rw.Wall.Id, rw.Wall.IsColumn, segment, normal, rw.Footprint is not null, material));
            }
        }

        return surfaces;
    }

    /// <summary>Nombres de los materiales de la escena que no tienen propiedades eléctricas y por lo tanto no reflejan.</summary>
    public static IReadOnlyList<string> NonReflectingMaterials(SimulationScene scene) =>
        [.. scene.Walls.Where(w => w.Material.Reflection is null).Select(w => w.Material.Name).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)];
}
