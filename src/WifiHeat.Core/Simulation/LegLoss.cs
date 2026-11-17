using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;

namespace WifiHeat.Core.Simulation;

/// <summary>
/// Pérdida por materiales de un tramo en los modelos con difracción. Vale la regla de uniones de <see cref="PathCrossings"/>,
/// con tres diferencias respecto del modelo de línea recta:
/// 1. El tramo se recorta <see cref="TrimM"/> en el extremo que es vértice, para no contar el obstáculo que rodea.
/// 2. Una columna cuenta solo si el tramo pasa por su interior. Correr a lo largo de una cara o tocar un vértice no cuenta.
/// 3. Un muro no cuenta si el tramo corre a lo largo de él, ni si lo toca a menos de <see cref="GrazeM"/> de un extremo
///    que es vértice difractante (trayecto rasante).
/// </summary>
public static class LegLoss
{
    /// <summary>Recorte del tramo en el extremo que es vértice (m).</summary>
    public const double TrimM = 2e-3;

    /// <summary>Distancia por debajo de la cual un trayecto se considera rasante a un vértice (m).</summary>
    public const double GrazeM = 1e-6;

    /// <param name="trimA">El extremo a es un vértice difractante.</param>
    /// <param name="trimB">El extremo b es un vértice difractante.</param>
    /// <param name="vertexPositions">Posiciones de los vértices difractantes de la escena.</param>
    public static double LossDb(SimulationScene scene, Band band, Vec2 a, Vec2 b, bool trimA, bool trimB, IReadOnlyList<Vec2> vertexPositions)
    {
        double length = Vec2.Distance(a, b);
        double ta = trimA ? TrimM : 0.0;
        double tb = trimB ? TrimM : 0.0;
        if (length <= ta + tb + 1e-9) return 0.0;

        var u = new Vec2((b.X - a.X) / length, (b.Y - a.Y) / length);
        var path = new Segment2(a + u * ta, b - u * tb);
        double pathLength = path.Length;

        List<PathCrossings.Contact>? contacts = null;
        foreach (ResolvedWall rw in scene.Walls)
        {
            List<(double From, double To)>? spans = null;
            if (rw.Footprint is not null)
            {
                foreach ((double t0, double t1) in rw.Footprint.InteriorIntervals(path))
                {
                    spans ??= [];
                    spans.Add((t0 * pathLength, t0 * pathLength));
                    spans.Add((t1 * pathLength, t1 * pathLength));
                }
            }
            else
            {
                foreach (Segment2 segment in rw.Wall.Segments)
                {
                    if (!SegmentIntersection.Intersects(path, segment)) continue;

                    (double From, double To) span = SegmentIntersection.ContactSpan(path, segment);
                    if (span.To - span.From > GrazeM) continue;

                    Vec2 contact = path.A + u * span.From;
                    if (GrazesAnEnd(contact, segment, vertexPositions)) continue;

                    (spans ??= []).Add(span);
                }
            }

            if (spans is not null) (contacts ??= []).Add(new PathCrossings.Contact(rw, spans));
        }

        return PathCrossings.TotalLossDb(contacts, band);
    }

    private static bool GrazesAnEnd(Vec2 contact, Segment2 segment, IReadOnlyList<Vec2> vertexPositions)
    {
        for (int i = 0; i < vertexPositions.Count; i++)
        {
            Vec2 q = vertexPositions[i];
            if (Vec2.Distance(contact, q) > GrazeM) continue;
            if (Vec2.Distance(q, segment.A) <= PathCrossings.JunctionToleranceM || Vec2.Distance(q, segment.B) <= PathCrossings.JunctionToleranceM) return true;
        }

        return false;
    }
}
