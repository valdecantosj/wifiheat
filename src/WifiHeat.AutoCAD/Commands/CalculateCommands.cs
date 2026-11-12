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
    private static readonly IOverlayRenderer Overlay = new SolidRunOverlayRenderer();

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
        int entities;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            DrawingStore.EnsureAll(db, tr);
            entities = Overlay.Render(db, tr, unit, grid);
            LegendRenderer.Render(db, tr, unit, grid.Spec, Legend.For(session.Settings));
            tr.Commit();
        }

        foreach (string warning in result.Warnings) ed.WriteMessage($"\n  [AVISO] {warning}");
        GridSpec spec = grid.Spec;
        ed.WriteMessage(string.Create(Es,
            $"\nWIFICALCULATE: {spec.CellCount} celdas ({spec.Columns} × {spec.Rows}, {spec.CellSizeM:0.##} m) · RSSI mín {grid.Min:0.0} dBm · máx {grid.Max:0.0} dBm · cálculo {watch.Elapsed.TotalMilliseconds:0} ms · {entities} entidades de overlay.\n"));
        ed.WriteMessage(area.Polygon is null
            ? "  Área de cobertura: rectángulo de los muros.\n"
            : $"  Área de cobertura: polilínea {area.Handle} ({grid.ValueCount} celdas con valor).\n");
    }
}
