using System.Diagnostics;
using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.AutoCAD.Reading;
using WifiHeat.AutoCAD.Rendering;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD.Commands;

public sealed class CalculateCommands
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");
    private static readonly IOverlayRenderer Overlay = new RasterOverlayRenderer();

    /// <summary>Valida, calcula la grilla y dibuja el mapa de calor y la leyenda.</summary>
    [CommandMethod("WIFICALCULATE")]
    public void Calculate()
    {
        Document doc = CoreApp.DocumentManager.MdiActiveDocument;
        Database db = doc.Database;
        Editor ed = doc.Editor;
        SimulationSession session = Sessions.For(doc);

        ValidationReport report = ValidateCommands.LoadWalls(doc);
        if (!report.IsValid)
        {
            ValidateCommands.Print(ed, "WIFICALCULATE", session, report);
            ed.WriteMessage("WIFICALCULATE: no se calcula hasta corregir los errores.\n");
            return;
        }

        DrawingUnit unit = DrawingStore.ReadUnit(db)!.Value;
        StoredArea area = AreaReader.Read(db, unit);
        session.SetCoverageArea(area.Polygon);
        if (area.Warning is not null) ed.WriteMessage($"\n  [AVISO] {area.Warning}");

        var watch = Stopwatch.StartNew();
        OperationResult<RssiGrid> result = session.Calculate();
        watch.Stop();
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors) ed.WriteMessage($"\nWIFICALCULATE: {error}");
            ed.WriteMessage("\n");
            return;
        }

        RssiGrid grid = result.Value!;
        var imageWatch = Stopwatch.StartNew();
        HeatmapImage image = session.Heatmap()!;
        (string imagePath, bool saved) = OverlayImagePath(doc);
        OverlayResult overlay;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            DrawingStore.EnsureAll(db, tr);
            overlay = Overlay.Render(db, tr, unit, image, imagePath);
            LegendRenderer.Render(db, tr, unit, grid.Spec, Legend.For(session.Settings));
            tr.Commit();
        }

        imageWatch.Stop();

        foreach (string warning in result.Warnings) ed.WriteMessage($"\n  [AVISO] {warning}");
        if (!saved) ed.WriteMessage("\n  [AVISO] El dibujo no está guardado: la imagen del mapa se guardó en la carpeta temporal. Guardar el dibujo y volver a calcular para dejarla junto al DWG.");
        if (overlay.ImagePath != imagePath) ed.WriteMessage($"\n  [AVISO] {imagePath} estaba en uso; la imagen se guardó con otro nombre.");

        GridSpec spec = grid.Spec;
        ed.WriteMessage(string.Create(Es,
            $"\nWIFICALCULATE: {spec.CellCount} celdas ({spec.Columns} × {spec.Rows}, {spec.CellSizeM:0.##} m) · RSSI mín {grid.Min:0.0} dBm · máx {grid.Max:0.0} dBm · cálculo {watch.Elapsed.TotalMilliseconds:0} ms · {overlay.Entities} entidad(es) de overlay.\n"));
        ed.WriteMessage(string.Create(Es,
            $"  Imagen: {image.Width} × {image.Height} px de {image.PixelSizeM * 100:0.##} cm · {imageWatch.Elapsed.TotalMilliseconds:0} ms · {overlay.ImagePath}\n"));
        ed.WriteMessage(area.Polygon is null
            ? "  Área de cobertura: rectángulo de los muros.\n"
            : $"  Área de cobertura: polilínea {area.Handle} ({grid.ValueCount} celdas con valor).\n");
    }

    /// <summary>Junto al DWG (&lt;dwg&gt;_wifiheat.png) o, si el dibujo no está guardado, en la carpeta temporal.</summary>
    private static (string Path, bool NextToDrawing) OverlayImagePath(Document doc)
    {
        bool saved = Convert.ToInt32(CoreApp.GetSystemVariable("DWGTITLED")) != 0;
        if (saved)
        {
            string dwg = doc.Database.Filename;
            return (Path.Combine(Path.GetDirectoryName(dwg)!, Path.GetFileNameWithoutExtension(dwg) + "_wifiheat.png"), true);
        }

        return (Path.Combine(Path.GetTempPath(), Path.GetFileNameWithoutExtension(doc.Name) + "_wifiheat.png"), false);
    }
}
