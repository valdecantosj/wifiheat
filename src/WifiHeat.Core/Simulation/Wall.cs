using WifiHeat.Core.Geometry;

namespace WifiHeat.Core.Simulation;

/// <summary>
/// Muro de simulación: un eje formado por uno o más segmentos (metros).
/// Material y espesor pueden faltar al leerse del DWG; la validación lo informa como FALTA PROPIEDAD.
/// El espesor se registra pero no modifica la pérdida.
/// </summary>
public sealed record Wall(string Id, IReadOnlyList<Segment2> Segments, string? MaterialId, double? ThicknessM)
{
    /// <summary>True si el trayecto toca el muro en algún segmento. Cuenta una vez por muro.</summary>
    public bool IsCrossedBy(Segment2 path)
    {
        foreach (Segment2 segment in Segments)
        {
            if (SegmentIntersection.Intersects(path, segment))
            {
                return true;
            }
        }

        return false;
    }
}
