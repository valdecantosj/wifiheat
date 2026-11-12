using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.AutoCAD.Reading;
using WifiHeat.AutoCAD.Rendering;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Walls;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD.Commands;

public sealed class AxisCommands
{
    private const int MaxListed = 30;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>
    /// Propone ejes para muros dibujados con sus dos caras, dentro de una ventana.
    /// Crea líneas nuevas en la capa de muros con el espesor estimado y sin material; no modifica las caras.
    /// </summary>
    [CommandMethod("WIFIEJES")]
    public void CreateAxes() => CommandGuard.Run("WIFIEJES", doc =>
    {
        Database db = doc.Database;
        Editor ed = doc.Editor;
        if (!DrawingContext.TryGetUnit(doc, "WIFIEJES", out DrawingUnit unit)) return;

        PromptPointResult first = ed.GetPoint(new PromptPointOptions("\nPrimera esquina de la ventana (una sola planta): "));
        if (first.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, "WIFIEJES"); return; }
        PromptPointResult second = ed.GetCorner(new PromptCornerOptions("\nEsquina opuesta: ", first.Value));
        if (second.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, "WIFIEJES"); return; }

        Matrix3d ucs = ed.CurrentUserCoordinateSystem;
        Point3d a = first.Value.TransformBy(ucs), b = second.Value.TransformBy(ucs);
        var window = new BoundingBox(
            DrawingUnits.ToMetres(unit, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
            DrawingUnits.ToMetres(unit, Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));

        Dictionary<string, List<FaceSegment>> byLayer = ReadFaces(db, unit, window);
        if (byLayer.Count == 0)
        {
            ed.WriteMessage("\nWIFIEJES: no hay líneas ni polilíneas enteras dentro de la ventana.\n");
            return;
        }

        var axesByLayer = byLayer.ToDictionary(kv => kv.Key, kv => WallAxisDetector.Detect(kv.Value).Axes.Count, StringComparer.OrdinalIgnoreCase);
        List<string> ranked = [.. byLayer.Keys
            .OrderByDescending(k => axesByLayer[k] > 0 && (k.Contains("muro", StringComparison.OrdinalIgnoreCase) || k.Contains("wall", StringComparison.OrdinalIgnoreCase)))
            .ThenByDescending(k => axesByLayer[k])
            .ThenByDescending(k => byLayer[k].Count)];
        string suggested = ranked[0];
        ed.WriteMessage($"\nDentro de la ventana, por capa: {string.Join(", ", ranked.Select(k => $"{k} ({byLayer[k].Count} segmentos, {axesByLayer[k]} ejes)"))}.");
        PromptResult layerAnswer = ed.GetString(new PromptStringOptions($"\nCapas de las caras de muro, separadas por coma, o * para todas <{suggested}>: ") { AllowSpaces = true });
        if (layerAnswer.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, "WIFIEJES"); return; }

        string answer = string.IsNullOrWhiteSpace(layerAnswer.StringResult) ? suggested : layerAnswer.StringResult.Trim();
        string[] layers = answer == "*" ? [.. byLayer.Keys] : answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string[] missing = [.. layers.Where(l => !byLayer.ContainsKey(l))];
        if (missing.Length > 0 || layers.Length == 0)
        {
            ed.WriteMessage($"\nWIFIEJES: sin segmentos dentro de la ventana en la capa '{string.Join("', '", missing)}'. No se creó ningún eje.\n");
            return;
        }

        List<FaceSegment> faces = [.. layers.SelectMany(l => byLayer[l])];

        AxisDetection result = WallAxisDetector.Detect(faces);
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            DrawingStore.EnsureAll(db, tr);
            BlockTableRecord modelSpace = ModelSpace.Open(db, tr, OpenMode.ForWrite);
            foreach (WallAxis axis in result.Axes)
            {
                var line = new Line(ModelSpace.ToDrawing(unit, axis.Axis.A), ModelSpace.ToDrawing(unit, axis.Axis.B))
                {
                    Layer = WifiHeatNames.WallLayer,
                };
                ModelSpace.Add(modelSpace, tr, line);
                WifiXData.WriteWall(line, "", Math.Round(axis.ThicknessM, 3));
            }

            tr.Commit();
        }

        ed.WriteMessage(string.Create(Es, $"\nWIFIEJES: {result.Axes.Count} eje(s) creados en {WifiHeatNames.WallLayer} a partir de {faces.Count} segmento(s)."));
        if (result.Axes.Count > 0)
        {
            ed.WriteMessage(string.Create(Es,
                $"\n  Espesor estimado: {result.Axes.Min(x => x.ThicknessM):0.###} a {result.Axes.Max(x => x.ThicknessM):0.###} m. Falta asignar el material con WIFIWALL (Enter en el espesor lo conserva)."));
        }

        PrintIds(ed, "Segmentos sin par", result.Unpaired);
        PrintIds(ed, "Segmentos con tramos sin par de más de 0,5 m", result.PartlyUnpaired);
        ed.WriteMessage("\n");
    });

    /// <summary>Líneas y polilíneas (tramos rectos) con todos sus extremos dentro de la ventana, fuera de las capas propias.</summary>
    private static Dictionary<string, List<FaceSegment>> ReadFaces(Database db, DrawingUnit unit, BoundingBox window)
    {
        var byLayer = new Dictionary<string, List<FaceSegment>>(StringComparer.OrdinalIgnoreCase);
        using Transaction tr = db.TransactionManager.StartTransaction();
        foreach (ObjectId id in ModelSpace.Open(db, tr, OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Curve curve) continue;
            if (curve.Layer.StartsWith("WIFIHEAT_", StringComparison.OrdinalIgnoreCase)) continue;

            var segments = new List<FaceSegment>();
            string handle = curve.Handle.ToString();
            if (curve is Line line)
            {
                segments.Add(new FaceSegment(handle, Segment(unit, line.StartPoint, line.EndPoint)));
            }
            else if (curve is Polyline polyline)
            {
                int vertices = polyline.NumberOfVertices;
                int count = polyline.Closed ? vertices : vertices - 1;
                for (int i = 0; i < count; i++)
                {
                    if (polyline.GetBulgeAt(i) != 0) continue;
                    segments.Add(new FaceSegment($"{handle}/{i}", Segment(unit, polyline.GetPoint3dAt(i), polyline.GetPoint3dAt((i + 1) % vertices))));
                }
            }
            else
            {
                continue;
            }

            IReadOnlyList<FaceSegment> inside = WallAxisDetector.InsideWindow(segments, window);
            if (inside.Count != segments.Count || inside.Count == 0) continue;

            if (!byLayer.TryGetValue(curve.Layer, out List<FaceSegment>? list)) byLayer[curve.Layer] = list = [];
            list.AddRange(inside);
        }

        tr.Commit();
        return byLayer;
    }

    private static Segment2 Segment(DrawingUnit unit, Point3d a, Point3d b) =>
        new(DrawingUnits.ToMetres(unit, a.X, a.Y), DrawingUnits.ToMetres(unit, b.X, b.Y));

    private static void PrintIds(Editor ed, string title, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return;
        string list = string.Join(", ", ids.Take(MaxListed));
        ed.WriteMessage($"\n  {title}: {ids.Count} ({list}{(ids.Count > MaxListed ? ", …" : "")}).");
    }
}
