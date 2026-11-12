using System.Globalization;
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
    private const int MaxListed = 30;
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>
    /// Propone ejes para muros dibujados con sus dos caras, dentro de una ventana.
    /// Crea líneas en la capa de muros con el espesor estimado y sin material; no modifica las caras.
    /// Al repetirlo sobre la misma ventana reemplaza los ejes generados antes.
    /// </summary>
    [CommandMethod("WIFIEJES")]
    public void CreateAxes() => CommandGuard.Run("WIFIEJES", doc =>
    {
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

        AutoCadWifiHeatService service = Sessions.Service(doc);
        OperationResult<AxisSurvey> surveyed = service.SurveyAxisLayers(window);
        CommandOutput.Problems(ed, "WIFIEJES", surveyed.Errors, surveyed.Warnings);
        if (!surveyed.Succeeded) return;

        AxisSurvey survey = surveyed.Value!;
        if (survey.Layers.Any(l => !l.ExplicitOnly))
        {
            ed.WriteMessage($"\nDentro de la ventana, por capa: {Describe(survey.Layers.Where(l => !l.ExplicitOnly))}.");
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
        if (layerAnswer.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, "WIFIEJES"); return; }

        string answer = string.IsNullOrWhiteSpace(layerAnswer.StringResult) ? suggested ?? "" : layerAnswer.StringResult.Trim();
        string[] layers = answer == "*"
            ? [.. survey.Layers.Where(l => !l.ExplicitOnly).Select(l => l.Layer)]
            : answer.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var gapOptions = new PromptDoubleOptions($"\nUnir tramos alineados separados hasta (m, máximo {AxisDetectionOptions.LargestMergeGapM:0})")
        {
            DefaultValue = AxisDefaults.MergeGapM,
            UseDefaultValue = true,
            AllowNegative = false,
            AllowZero = true,
        };
        PromptDoubleResult gapAnswer = ed.GetDouble(gapOptions);
        if (gapAnswer.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, "WIFIEJES"); return; }

        OperationResult<AxisGeneration> generated = service.GenerateAxes(window, layers, gapAnswer.Value);
        CommandOutput.Problems(ed, "WIFIEJES", generated.Errors, generated.Warnings);
        if (!generated.Succeeded) return;

        AxisGeneration result = generated.Value!;
        ed.WriteMessage(string.Create(Es,
            $"\nWIFIEJES: {result.AxisCount} eje(s) en {WifiHeatNames.WallLayer} a partir de {result.SegmentCount} segmento(s): {result.Created} creado(s), {result.Kept} conservado(s), {result.Removed} anterior(es) borrado(s). Umbral de unión {result.MergeGapM:0.00} m."));
        if (result.AxisCount > 0)
        {
            ed.WriteMessage(string.Create(Es,
                $"\n  Espesor estimado: {result.MinThicknessM:0.###} a {result.MaxThicknessM:0.###} m. Falta asignar el material con WIFIWALL (Enter en el espesor lo conserva)."));
        }

        PrintIds(ed, "Segmentos sin par", result.Unpaired);
        PrintIds(ed, "Segmentos con tramos sin par de más de 0,5 m", result.PartlyUnpaired);
        ed.WriteMessage("\n");
    });

    private static string Describe(IEnumerable<AxisLayerInfo> layers) =>
        string.Join(", ", layers.Select(l => $"{l.Layer} ({l.SegmentCount} segmentos, {l.AxisCount} ejes)"));

    private static void PrintIds(Editor ed, string title, IReadOnlyList<string> ids)
    {
        if (ids.Count == 0) return;
        string list = string.Join(", ", ids.Take(MaxListed));
        ed.WriteMessage($"\n  {title}: {ids.Count} ({list}{(ids.Count > MaxListed ? ", …" : "")}).");
    }
}
