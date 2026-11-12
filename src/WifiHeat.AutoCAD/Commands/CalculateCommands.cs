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
    private const string ResolutionKeyword = "Resolucion";
    private const string FadeKeyword = "Atenuacion";
    private const string CalculateKeyword = "Calcular";
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>Valida, calcula la grilla y dibuja el mapa de calor y la leyenda.</summary>
    [CommandMethod("WIFICALCULATE")]
    public void Calculate() => CommandGuard.Run("WIFICALCULATE", doc =>
    {
        Database db = doc.Database;
        Editor ed = doc.Editor;
        SimulationSession session = Sessions.For(doc);

        if (!AskOptions(ed, session)) { CommandGuard.Cancelled(ed, "WIFICALCULATE"); return; }

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
        (string folder, string baseName, bool saved) = OverlayImageLocation(doc);
        OverlayResult overlay;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            DrawingStore.EnsureAll(db, tr);
            overlay = RasterOverlayRenderer.Render(db, tr, unit, image, folder, baseName, session.OverlayFadePercent);
            LegendRenderer.Render(db, tr, unit, grid.Spec, Legend.For(session.Settings), session.OverlayFadePercent);
            tr.Commit();
        }

        imageWatch.Stop();

        foreach (string warning in result.Warnings.Concat(overlay.Warnings)) ed.WriteMessage($"\n  [AVISO] {warning}");
        if (!saved) ed.WriteMessage("\n  [AVISO] El dibujo no está guardado: la imagen del mapa se guardó en la carpeta temporal. Guardar el dibujo y volver a calcular para dejarla junto al DWG.");

        GridSpec spec = grid.Spec;
        ed.WriteMessage(string.Create(Es,
            $"\nWIFICALCULATE: {spec.CellCount} celdas ({spec.Columns} × {spec.Rows}, {spec.CellSizeM:0.##} m) · RSSI mín {grid.Min:0.0} dBm · máx {grid.Max:0.0} dBm · cálculo {watch.Elapsed.TotalMilliseconds:0} ms · {overlay.Entities} entidad(es) de overlay.\n"));
        ed.WriteMessage(string.Create(Es,
            $"  Imagen: {image.Width} × {image.Height} px de {image.PixelSizeM * 100:0.##} cm · atenuación {session.OverlayFadePercent} % · {imageWatch.Elapsed.TotalMilliseconds:0} ms · {overlay.ImagePath ?? "sin imagen"}\n"));
        ed.WriteMessage(area.Polygon is null
            ? "  Área de cobertura: rectángulo de los muros.\n"
            : $"  Área de cobertura: polilínea {area.Handle} ({grid.ValueCount} celdas con valor).\n");
    });

    /// <summary>
    /// Enter calcula con los valores vigentes. Resolución y Atenuación cambian el valor de la sesión y vuelven a preguntar.
    /// Devuelve false si el usuario cancela.
    /// </summary>
    private static bool AskOptions(Editor ed, SimulationSession session)
    {
        while (true)
        {
            var options = new PromptKeywordOptions(string.Create(Es,
                $"\nResolución {session.CellSizeM:0.00} m · atenuación {session.OverlayFadePercent} %. Elegir opción")) { AllowNone = true };
            options.Keywords.Add(CalculateKeyword);
            options.Keywords.Add(ResolutionKeyword, ResolutionKeyword, "Resolución");
            options.Keywords.Add(FadeKeyword, FadeKeyword, "Atenuación");
            options.Keywords.Default = CalculateKeyword;
            PromptResult answer = ed.GetKeywords(options);
            if (answer.Status == PromptStatus.None || (answer.Status == PromptStatus.OK && answer.StringResult == CalculateKeyword)) return true;
            if (answer.Status != PromptStatus.OK) return false;

            if (answer.StringResult == ResolutionKeyword)
            {
                PromptDoubleResult size = ed.GetDouble(new PromptDoubleOptions(string.Create(Es,
                    $"\nTamaño de celda en metros ({SimulationSession.MinCellSizeM:0.00} a {SimulationSession.MaxCellSizeM:0})"))
                {
                    DefaultValue = session.CellSizeM,
                    UseDefaultValue = true,
                    AllowNegative = false,
                    AllowZero = false,
                });
                if (size.Status != PromptStatus.OK) return false;
                Report(ed, session.SetResolution(size.Value).Errors);
            }
            else
            {
                PromptIntegerResult fade = ed.GetInteger(new PromptIntegerOptions(
                    $"\nAtenuación del mapa en % ({OverlayFade.MinPercent} = colores plenos, máximo {OverlayFade.MaxPercent})")
                {
                    DefaultValue = session.OverlayFadePercent,
                    UseDefaultValue = true,
                    AllowNegative = false,
                });
                if (fade.Status != PromptStatus.OK) return false;
                Report(ed, session.SetOverlayFade(fade.Value).Errors);
            }
        }
    }

    private static void Report(Editor ed, IReadOnlyList<string> errors)
    {
        foreach (string error in errors) ed.WriteMessage($"\nWIFICALCULATE: {error}");
    }

    /// <summary>Junto al DWG o, si el dibujo no está guardado, en la carpeta temporal.</summary>
    private static (string Folder, string BaseName, bool NextToDrawing) OverlayImageLocation(Document doc)
    {
        bool saved = Convert.ToInt32(CoreApp.GetSystemVariable("DWGTITLED")) != 0;
        if (saved)
        {
            string dwg = doc.Database.Filename;
            return (Path.GetDirectoryName(dwg)!, Path.GetFileNameWithoutExtension(dwg), true);
        }

        return (Path.GetTempPath(), Path.GetFileNameWithoutExtension(doc.Name), false);
    }
}
