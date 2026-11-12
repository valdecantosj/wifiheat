using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.AutoCAD.Reading;
using WifiHeat.Core.Materials;
using Material = WifiHeat.Core.Materials.Material;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD.Commands;

public sealed class WallCommands
{
    public const double DefaultThicknessM = 0.15;

    /// <summary>Asigna material y espesor a líneas o polilíneas y las pasa a la capa de muros.</summary>
    [CommandMethod("WIFIWALL")]
    public void AssignWall() => CommandGuard.Run("WIFIWALL", doc =>
    {
        Database db = doc.Database;
        Editor ed = doc.Editor;
        MaterialCatalog catalog = MaterialCatalogFile.Catalog;

        var selectionOptions = new PromptSelectionOptions { MessageForAdding = "\nSeleccionar líneas o polilíneas de muro" };
        var filter = new SelectionFilter([new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE")]);
        PromptSelectionResult selection = ed.GetSelection(selectionOptions, filter);
        if (selection.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, "WIFIWALL"); return; }

        string ids = string.Join("/", catalog.All.Select(m => m.Id));
        PromptResult materialAnswer = ed.GetString(new PromptStringOptions($"\nMaterial [{ids}]: ") { AllowSpaces = false });
        if (materialAnswer.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, "WIFIWALL"); return; }
        if (!catalog.TryGet(materialAnswer.StringResult.Trim(), out Material material))
        {
            ed.WriteMessage($"\nWIFIWALL: el material '{materialAnswer.StringResult}' no existe. Valores posibles: {ids}. No se modificó ningún muro.\n");
            return;
        }

        var thicknessOptions = new PromptDoubleOptions(string.Create(CultureInfo.GetCultureInfo("es-AR"),
            $"\nEspesor en metros o Enter para conservar el de cada muro ({DefaultThicknessM:0.00} si no tiene)"))
        {
            AllowNegative = false,
            AllowZero = false,
            AllowNone = true,
        };
        PromptDoubleResult thicknessAnswer = ed.GetDouble(thicknessOptions);
        if (thicknessAnswer.Status is not (PromptStatus.OK or PromptStatus.None)) { CommandGuard.Cancelled(ed, "WIFIWALL"); return; }
        double? thickness = thicknessAnswer.Status == PromptStatus.OK ? thicknessAnswer.Value : null;

        int count = 0;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            DrawingStore.EnsureAll(db, tr);
            foreach (ObjectId id in selection.Value.GetObjectIds())
            {
                var entity = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                double value = thickness ?? WifiXData.ReadWall(entity).ThicknessM ?? DefaultThicknessM;
                WifiXData.WriteWall(entity, material.Id, value);
                entity.Layer = WifiHeatNames.WallLayer;
                count++;
            }

            tr.Commit();
        }

        string thicknessText = thickness is null ? "espesor conservado" : string.Create(CultureInfo.GetCultureInfo("es-AR"), $"espesor {thickness:0.###} m");
        ed.WriteMessage($"\nWIFIWALL: {count} muro(s) con material {material.Name} ({material.Id}) y {thicknessText}.\n");
    });
}
