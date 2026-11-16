using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.AutoCAD.Reading;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Service;
using WifiHeat.Core.Workflow;
using Material = WifiHeat.Core.Materials.Material;

namespace WifiHeat.AutoCAD.Commands;

public sealed class WallCommands
{
    private const string Command = "WIFIWALL";
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>
    /// Asigna material y espesor a líneas o polilíneas y las pasa a la capa de muros.
    /// Si la selección trae las dos caras de un mismo muro, ofrece reemplazarlas por un eje.
    /// </summary>
    [CommandMethod(Command)]
    public void AssignWall() => CommandGuard.Run(Command, doc =>
    {
        Editor ed = doc.Editor;
        MaterialCatalog catalog = MaterialCatalogFile.Catalog;

        var selectionOptions = new PromptSelectionOptions { MessageForAdding = "\nSeleccionar líneas o polilíneas de muro" };
        var filter = new SelectionFilter([new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE")]);
        PromptSelectionResult selection = ed.GetSelection(selectionOptions, filter);
        if (selection.Status == PromptStatus.Error)
        {
            ed.WriteMessage($"\n{Command}: no se eligió ninguna línea ni polilínea.\n");
            return;
        }

        if (selection.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, Command); return; }

        string ids = string.Join("/", catalog.All.Select(m => m.Id));
        PromptResult materialAnswer = ed.GetString(new PromptStringOptions($"\nMaterial [{ids}]: ") { AllowSpaces = false });
        if (materialAnswer.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, Command); return; }
        if (!catalog.TryGet(materialAnswer.StringResult.Trim(), out Material material))
        {
            ed.WriteMessage($"\n{Command}: {ValidationIssue.UnknownMaterial}: '{materialAnswer.StringResult}' no está en el catálogo. Valores posibles: {ids}. No se modificó ningún muro.\n");
            return;
        }

        string[] handles = [.. selection.Value.GetObjectIds().Select(id => id.Handle.ToString())];
        AutoCadWifiHeatService service = Sessions.Service(doc);
        if (!AskJoinFaces(ed, service.FindFacePairs(handles), out bool join)) { CommandGuard.Cancelled(ed, Command); return; }

        var thicknessOptions = new PromptDoubleOptions(string.Create(Es,
            $"\nEspesor en metros o Enter para conservar el de cada muro ({WifiHeatService.DefaultThicknessM:0.00} si no tiene)"))
        {
            AllowNegative = false,
            AllowZero = false,
            AllowNone = true,
        };
        PromptDoubleResult thicknessAnswer = ed.GetDouble(thicknessOptions);
        if (thicknessAnswer.Status is not (PromptStatus.OK or PromptStatus.None)) { CommandGuard.Cancelled(ed, Command); return; }
        double? thickness = thicknessAnswer.Status == PromptStatus.OK ? thicknessAnswer.Value : null;
        string thicknessText = thickness is null ? "espesor conservado" : string.Create(Es, $"espesor {thickness:0.###} m");

        if (join)
        {
            OperationResult<FaceJoin> joined = service.AssignMaterialJoiningFaces(handles, material.Id, thickness);
            CommandOutput.Problems(ed, Command, joined.Errors, joined.Warnings);
            if (!joined.Succeeded) return;

            FaceJoin j = joined.Value!;
            ed.WriteMessage(string.Create(Es,
                $"\n{Command}: {j.AxisCount} eje(s) con material {material.Name} ({material.Id}) a partir de {j.FaceCount} cara(s): {j.AxesCreated} creado(s), {j.AxesKept} que ya existía(n). Espesor medido {Range(j.MinThicknessM, j.MaxThicknessM)} m. Las caras siguen en su capa."));
            ed.WriteMessage($"\n  {j.SingleWalls} muro(s) sin par con material {material.Name} y {thicknessText}.\n");
            return;
        }

        OperationResult<int> result = service.AssignMaterial(handles, material.Id, thickness);
        CommandOutput.Problems(ed, Command, result.Errors, result.Warnings);
        if (!result.Succeeded) return;

        ed.WriteMessage($"\n{Command}: {result.Value} muro(s) con material {material.Name} ({material.Id}) y {thicknessText}.\n");
    });

    private static string Range(double min, double max) =>
        Math.Abs(max - min) < 0.0005 ? string.Create(Es, $"{min:0.###}") : string.Create(Es, $"{min:0.###} a {max:0.###}");

    /// <summary>Pregunta solo si hay pares de caras. Devuelve false si el usuario cancela.</summary>
    private static bool AskJoinFaces(Editor ed, FacePairSurvey pairs, out bool join)
    {
        join = false;
        if (!pairs.HasPairs) return true;

        ed.WriteMessage(string.Create(Es,
            $"\nLa selección tiene {pairs.PairCount} par(es) de caras de un mismo muro (separación {Range(pairs.MinThicknessM, pairs.MaxThicknessM)} m). Si quedan como dos muros, la pérdida se suma dos veces."));
        return CommandOutput.AskYesNo(ed, "\nUnir caras", defaultYes: true, out join);
    }
}
