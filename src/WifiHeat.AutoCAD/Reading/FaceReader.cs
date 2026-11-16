using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using WifiHeat.AutoCAD.Rendering;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Walls;

namespace WifiHeat.AutoCAD.Reading;

/// <summary>Geometría que usa la generación de ejes: caras de muro del plano y ejes ya generados.</summary>
public static class FaceReader
{
    /// <summary>Líneas y polilíneas (tramos rectos) con todos sus extremos dentro de la zona, fuera de las capas propias.</summary>
    public static IReadOnlyList<LayerFaces> ReadFaces(Database db, Transaction tr, DrawingUnit unit, BoundingBox window)
    {
        var byLayer = new Dictionary<string, List<FaceSegment>>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in ModelSpace.Open(db, tr, OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Curve curve) continue;
            if (curve is not (Line or Polyline)) continue;
            if (curve.Layer.StartsWith("WIFIHEAT_", StringComparison.OrdinalIgnoreCase)) continue;

            List<Segment2> straight = StraightSegments(unit, curve);
            string handle = curve.Handle.ToString();
            List<FaceSegment> segments = curve is Line
                ? [.. straight.Select(s => new FaceSegment(handle, s))]
                : [.. straight.Select((s, i) => new FaceSegment($"{handle}/{i}", s))];

            IReadOnlyList<FaceSegment> inside = WallAxisDetector.InsideWindow(segments, window);
            if (inside.Count != segments.Count || inside.Count == 0) continue;

            if (!byLayer.TryGetValue(curve.Layer, out List<FaceSegment>? list)) byLayer[curve.Layer] = list = [];
            list.AddRange(inside);
        }

        return [.. byLayer.Select(kv => new LayerFaces(kv.Key, kv.Value))];
    }

    /// <summary>Líneas de la capa de muros con la marca de eje generado, enteras dentro de la zona.</summary>
    public static IReadOnlyList<GeneratedAxis> ReadGeneratedAxes(Database db, Transaction tr, DrawingUnit unit, BoundingBox window)
    {
        var axes = new List<GeneratedAxis>();
        foreach (ObjectId id in ModelSpace.Open(db, tr, OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Line line) continue;
            if (!string.Equals(line.Layer, WifiHeatNames.WallLayer, StringComparison.OrdinalIgnoreCase)) continue;
            if (!WifiXData.IsGeneratedAxis(line)) continue;

            var face = new FaceSegment(line.Handle.ToString(), Segment(unit, line.StartPoint, line.EndPoint));
            if (WallAxisDetector.InsideWindow([face], window).Count == 1) axes.Add(new GeneratedAxis(face.Id, face.Segment));
        }

        return axes;
    }

    /// <summary>Tramos rectos de una línea o polilínea, en metros. Los arcos de una polilínea se omiten.</summary>
    public static List<Segment2> StraightSegments(DrawingUnit unit, Curve curve)
    {
        var segments = new List<Segment2>();
        if (curve is Line line)
        {
            segments.Add(Segment(unit, line.StartPoint, line.EndPoint));
        }
        else if (curve is Polyline polyline)
        {
            int vertices = polyline.NumberOfVertices;
            int count = polyline.Closed ? vertices : vertices - 1;
            for (int i = 0; i < count; i++)
            {
                if (polyline.GetBulgeAt(i) != 0) continue;
                segments.Add(Segment(unit, polyline.GetPoint3dAt(i), polyline.GetPoint3dAt((i + 1) % vertices)));
            }
        }

        return segments;
    }

    private static Segment2 Segment(DrawingUnit unit, Point3d a, Point3d b) =>
        new(DrawingUnits.ToMetres(unit, a.X, a.Y), DrawingUnits.ToMetres(unit, b.X, b.Y));
}
