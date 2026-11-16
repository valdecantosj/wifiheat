using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;

namespace WifiHeat.Core.Walls;

/// <summary>Entidad del plano (línea o polilínea) con sus tramos rectos, en metros.</summary>
public sealed record FaceEntity(string Id, IReadOnlyList<Segment2> Segments);

/// <summary>
/// Pares de caras entre entidades. Los ejes nombran entidades, no tramos.
/// Paired: entidades con algún tramo emparejado. Single: sin ningún par.
/// WithLeftover: emparejadas que además tienen un tramo sin par más largo que <see cref="AxisDetectionOptions.ReportGapM"/>.
/// </summary>
public sealed record FacePairs(
    IReadOnlyList<WallAxis> Axes,
    IReadOnlyList<string> Paired,
    IReadOnlyList<string> Single,
    IReadOnlyList<string> WithLeftover);

/// <summary>Aplica <see cref="WallAxisDetector"/> a un conjunto de entidades y resume el resultado por entidad.</summary>
public static class FacePairing
{
    private const double Tiny = 1e-9;

    public static FacePairs Detect(IReadOnlyList<FaceEntity> entities, AxisDetectionOptions? options = null)
    {
        options ??= new AxisDetectionOptions();
        var faces = new List<FaceSegment>();
        foreach (FaceEntity entity in entities)
        {
            for (int i = 0; i < entity.Segments.Count; i++) faces.Add(new FaceSegment(FaceId(entity, i), entity.Segments[i]));
        }

        AxisDetection detection = WallAxisDetector.Detect(faces, options);
        var unpaired = new HashSet<string>(detection.Unpaired, StringComparer.Ordinal);
        var partly = new HashSet<string>(detection.PartlyUnpaired, StringComparer.Ordinal);

        var paired = new List<string>();
        var single = new List<string>();
        var leftover = new List<string>();
        foreach (FaceEntity entity in entities)
        {
            bool anyPaired = false, anyLeftover = false;
            for (int i = 0; i < entity.Segments.Count; i++)
            {
                double length = entity.Segments[i].Length;
                if (length <= Tiny) continue;

                string id = FaceId(entity, i);
                if (!unpaired.Contains(id)) anyPaired = true;
                if (partly.Contains(id) || (unpaired.Contains(id) && length > options.ReportGapM)) anyLeftover = true;
            }

            if (!anyPaired) single.Add(entity.Id);
            else
            {
                paired.Add(entity.Id);
                if (anyLeftover) leftover.Add(entity.Id);
            }
        }

        List<WallAxis> axes = [.. detection.Axes.Select(a =>
            new WallAxis(a.Axis, a.ThicknessM, [.. a.FaceIds.Select(WallAxisDetector.EntityId).Distinct(StringComparer.Ordinal)]))];
        return new FacePairs(axes, paired, single, leftover);
    }

    private static string FaceId(FaceEntity entity, int index) => entity.Segments.Count == 1 ? entity.Id : $"{entity.Id}/{index}";
}

/// <summary>
/// Muros distintos, paralelos y a no más de la separación máxima de caras: probablemente las dos caras de un mismo muro,
/// que así suman la pérdida dos veces. Convertible: ninguno tiene además un tramo largo sin par, de modo que
/// reemplazarlos por el eje no deja un hueco.
/// </summary>
public sealed record ParallelWallGroup(Segment2 Axis, double SeparationM, IReadOnlyList<string> WallIds, bool Convertible);

public static class ParallelWalls
{
    public static IReadOnlyList<ParallelWallGroup> Find(IReadOnlyList<Wall> walls)
    {
        if (walls.Count < 2) return [];

        FacePairs pairs = FacePairing.Detect(
            [.. walls.Where(w => !w.IsColumn).Select(w => new FaceEntity(w.Id, w.Segments))],
            new AxisDetectionOptions { PairWithinSameEntity = false });
        var leftover = new HashSet<string>(pairs.WithLeftover, StringComparer.Ordinal);
        return [.. pairs.Axes.Select(a => new ParallelWallGroup(a.Axis, a.ThicknessM, a.FaceIds, !a.FaceIds.Any(leftover.Contains)))];
    }
}
