using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.AutoCAD.Reading;
using WifiHeat.Core.Geometry;

namespace WifiHeat.AutoCAD.Commands;

public sealed class AreaCommands
{
    private const string RectangleKeyword = "Rectangulo";
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>Define el área de cobertura con una polilínea cerrada, o vuelve al rectángulo de los muros.</summary>
    [CommandMethod("WIFIAREA")]
    public void DefineArea() => CommandGuard.Run("WIFIAREA", doc =>
    {
        Database db = doc.Database;
        Editor ed = doc.Editor;
        if (!DrawingContext.TryGetUnit(doc, "WIFIAREA", out DrawingUnit unit)) return;

        var options = new PromptEntityOptions("\nSeleccionar polilínea cerrada del área de cobertura") { AppendKeywordsToMessage = true };
        options.Keywords.Add(RectangleKeyword, RectangleKeyword, "Rectángulo");
        options.SetRejectMessage("\nDebe ser una polilínea.");
        options.AddAllowedClass(typeof(Polyline), true);
        PromptEntityResult answer = ed.GetEntity(options);

        if (answer.Status == PromptStatus.Keyword)
        {
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DrawingStore.WriteAreaId(db, tr, null);
                tr.Commit();
            }

            Sessions.For(doc).SetCoverageArea(null);
            ed.WriteMessage("\nWIFIAREA: se usa el rectángulo de los muros.\n");
            return;
        }

        if (answer.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, "WIFIAREA"); return; }

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            var polyline = (Polyline)tr.GetObject(answer.ObjectId, OpenMode.ForRead);
            if (!AreaReader.TryPolygon(polyline, unit, out Polygon2? polygon, out string? error))
            {
                ed.WriteMessage($"\nWIFIAREA: la polilínea {polyline.Handle} {error}. No se cambió el área.\n");
                return;
            }

            DrawingStore.WriteAreaId(db, tr, answer.ObjectId);
            tr.Commit();

            Sessions.For(doc).SetCoverageArea(polygon);
            ed.WriteMessage(string.Create(Es,
                $"\nWIFIAREA: área de cobertura = polilínea {polyline.Handle}, {polygon!.Vertices.Count} vértices, {polygon.AreaM2:0.##} m², de {polygon.Bounds.Min} a {polygon.Bounds.Max} m.\n"));
        }
    });
}
