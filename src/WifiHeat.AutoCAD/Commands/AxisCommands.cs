using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Walls;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.Commands;

public sealed class AxisCommands
{
    private const string Command = "WIFIEJES";
    private const string LinesKeyword = "Lineas";
    private const int MaxListed = 30;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>
    /// Propone ejes para muros dibujados con sus dos caras, dentro de la zona de una planta o entre las líneas elegidas.
    /// Crea líneas en la capa de muros con el espesor estimado y sin material; no modifica las caras.
    /// Al repetirlo sobre la misma zona reemplaza los ejes generados antes.
    /// </summary>
    [CommandMethod(Command)]
    public void CreateAxes() => CommandGuard.Run(Command, doc =>
    {
        Editor ed = doc.Editor;
        if (!DrawingContext.TryGetUnit(doc, Command, out DrawingUnit unit)) return;

        AutoCadWifiHeatService service = Sessions.Service(doc);
        var firstOptions = new PromptPointOptions("\nZona de la planta (rectángulo que encierre una sola planta). Primera esquina o");
        firstOptions.Keywords.Add(LinesKeyword, LinesKeyword, "Líneas");
        PromptPointResult first = ed.GetPoint(firstOptions);
        if (first.Status == PromptStatus.Keyword)
        {
            FromSelection(ed, service);
            return;
        }

        if (first.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, Command); return; }
        PromptPointResult second = ed.GetCorner(new PromptCornerOptions("\nEsquina opuesta: ", first.Value));
        if (second.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, Command); return; }

        Matrix3d ucs = ed.CurrentUserCoordinateSystem;
        Point3d a = first.Value.TransformBy(ucs), b = second.Value.TransformBy(ucs);
        var window = new BoundingBox(
            DrawingUnits.ToMetres(unit, Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)),
            DrawingUnits.ToMetres(unit, Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));

        OperationResult<AxisSurvey> surveyed = service.SurveyAxisLayers(window);
        CommandOutput.Problems(ed, Command, surveyed.Errors, surveyed.Warnings);
        if (!surveyed.Succeeded) return;

        AxisSurvey survey = surveyed.Value!;
        if (survey.Layers.Any(l => !l.ExplicitOnly))
        {
            ed.WriteMessage($"\nDentro de la zona, por capa: {Describe(survey.Layers.Where(l => !l.ExplicitOnly))}.");
        }

        if (survey.Layers.Any(l => l.ExplicitOnly))
        {
            ed.WriteMessage($"\nSolo si se nombran: {Describe(survey.Layers.Where(l => l.ExplicitOnly))}.");
        }

        string? suggested = survey.SuggestedLayer;
        string prompt = suggested is null
            ? "\nCapas de las caras de muro, separadas por coma: "
            : $"\nCapas de las caras de muro, separadas por coma, o * para todas las habituales <{suggested}>: ";
        PromptResult layerAnswer = ed.GetString(new PromptStringOptions(prompt) { AllowSpaces = true });
        if (layerAnswer.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, Command); return; }

        string answer = string.IsNullOrWhiteSpace(layerAnswer.StringResult) ? suggested ?? "" : layerAnswer.StringResult.Trim();
        string[] layers = answer == "*"
            ? [.. survey.Layers.Where(l => !l.ExplicitOnly).Select(l => l.Layer)]
            : answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (!AskMergeGap(ed, out double gap)) { CommandGuard.Cancelled(ed, Command); return; }

        Print(ed, service.GenerateAxes(window, layers, gap), fromSelection: false);
    });

    /// <summary>Aplica el detector solo a las líneas y polilíneas elegidas, de cualquier capa.</summary>
    private static void FromSelection(Editor ed, AutoCadWifiHeatService service)
    {
        var options = new PromptSelectionOptions { MessageForAdding = "\nSeleccionar las líneas de las caras de los muros (de cualquier capa)" };
        var filter = new SelectionFilter([new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE")]);
        PromptSelectionResult selection = ed.GetSelection(options, filter);
        if (selection.Status == PromptStatus.Error)
        {
            ed.WriteMessage($"\n{Command}: no se eligió ninguna línea ni polilínea. No se creó ningún eje.\n");
            return;
        }

        if (selection.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, Command); return; }
        if (!AskMergeGap(ed, out double gap)) { CommandGuard.Cancelled(ed, Command); return; }

        string[] handles = [.. selection.Value.GetObjectIds().Select(id => id.Handle.ToString())];
        Print(ed, service.GenerateAxesFromSelection(handles, gap), fromSelection: true);
    }

    private static bool AskMergeGap(Editor ed, out double gap)
    {
        var options = new PromptDoubleOptions($"\nUnir tramos alineados separados hasta (m, máximo {AxisDetectionOptions.LargestMergeGapM:0})")
        {
            DefaultValue = AxisDefaults.MergeGapM,
            UseDefaultValue = true,
            AllowNegative = false,
            AllowZero = true,
        };
        PromptDoubleResult answer = ed.GetDouble(options);
        gap = answer.Value;
        return answer.Status == PromptStatus.OK;
    }

    private static void Print(Editor ed, OperationResult<AxisGeneration> generated, bool fromSelection)
    {
        CommandOutput.Problems(ed, Command, generated.Errors, generated.Warnings);
        if (!generated.Succeeded) return;

        AxisGeneration result = generated.Value!;
        string source = fromSelection ? "de las líneas elegidas" : "";
        ed.WriteMessage(string.Create(Es,
            $"\n{Command}: {result.AxisCount} eje(s) en {WifiHeatNames.WallLayer} a partir de {result.SegmentCount} segmento(s){(source.Length > 0 ? " " + source : "")}: {result.Created} creado(s), {result.Kept} conservado(s), {result.Removed} anterior(es) borrado(s). Umbral de unión {result.MergeGapM:0.00} m."));
        if (result.AxisCount > 0)
        {
            ed.WriteMessage(string.Create(Es,
                $"\n  Espesor estimado: {result.MinThicknessM:0.###} a {result.MaxThicknessM:0.###} m. Falta asignar el material con WIFIWALL (Enter en el espesor lo conserva)."));
        }

        PrintIds(ed, fromSelection ? "Líneas sin par" : "Segmentos sin par", result.Unpaired);
        PrintIds(ed, fromSelection ? "Líneas con tramos sin par de más de 0,5 m" : "Segmentos con tramos sin par de más de 0,5 m", result.PartlyUnpaired);
        PrintIds(ed, "Figuras cerradas pequeñas que no se usaron como caras (posibles columnas; marcarlas con WIFIWALL y «columna»)", result.SmallClosedFigures);
        ed.WriteMessage("\n");
    }

    private static string Describe(IEnumerable<AxisLayerInfo> layers) =>
        string.Join(", ", layers.Select(l => $"{l.Layer} ({l.SegmentCount} segmentos, {l.AxisCount} ejes)"));

    private static void PrintIds(Editor ed, string title, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return;
        string list = string.Join(", ", ids.Take(MaxListed));
        ed.WriteMessage($"\n  {title}: {ids.Count} ({list}{(ids.Count > MaxListed ? ", …" : "")}).");
    }
}
