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
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>Asigna material y espesor a líneas o polilíneas y las pasa a la capa de muros.</summary>
    [CommandMethod("WIFIWALL")]
    public void AssignWall() => CommandGuard.Run("WIFIWALL", doc =>
    {
        Editor ed = doc.Editor;
        MaterialCatalog catalog = MaterialCatalogFile.Catalog;

        var selectionOptions = new PromptSelectionOptions { MessageForAdding = "\nSeleccionar líneas o polilíneas de muro" };
        var filter = new SelectionFilter([new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE")]);
        PromptSelectionResult selection = ed.GetSelection(selectionOptions, filter);
        if (selection.Status == PromptStatus.Error)
        {
            ed.WriteMessage("\nWIFIWALL: no se eligió ninguna línea ni polilínea.\n");
            return;
        }

        if (selection.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, "WIFIWALL"); return; }

        string ids = string.Join("/", catalog.All.Select(m => m.Id));
        PromptResult materialAnswer = ed.GetString(new PromptStringOptions($"\nMaterial [{ids}]: ") { AllowSpaces = false });
        if (materialAnswer.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, "WIFIWALL"); return; }
        if (!catalog.TryGet(materialAnswer.StringResult.Trim(), out Material material))
        {
            ed.WriteMessage($"\nWIFIWALL: {ValidationIssue.UnknownMaterial}: '{materialAnswer.StringResult}' no está en el catálogo. Valores posibles: {ids}. No se modificó ningún muro.\n");
            return;
        }

        var thicknessOptions = new PromptDoubleOptions(string.Create(Es,
            $"\nEspesor en metros o Enter para conservar el de cada muro ({WifiHeatService.DefaultThicknessM:0.00} si no tiene)"))
        {
            AllowNegative = false,
            AllowZero = false,
            AllowNone = true,
        };
        PromptDoubleResult thicknessAnswer = ed.GetDouble(thicknessOptions);
        if (thicknessAnswer.Status is not (PromptStatus.OK or PromptStatus.None)) { CommandGuard.Cancelled(ed, "WIFIWALL"); return; }
        double? thickness = thicknessAnswer.Status == PromptStatus.OK ? thicknessAnswer.Value : null;

        string[] handles = [.. selection.Value.GetObjectIds().Select(id => id.Handle.ToString())];
        OperationResult<int> result = Sessions.Service(doc).AssignMaterial(handles, materialAnswer.StringResult, thickness);
        CommandOutput.Problems(ed, "WIFIWALL", result.Errors, result.Warnings);
        if (!result.Succeeded) return;

        string thicknessText = thickness is null ? "espesor conservado" : string.Create(Es, $"espesor {thickness:0.###} m");
        ed.WriteMessage($"\nWIFIWALL: {result.Value} muro(s) con material {material.Name} ({material.Id}) y {thicknessText}.\n");
    });
}
