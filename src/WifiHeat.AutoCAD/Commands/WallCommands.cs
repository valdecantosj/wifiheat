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
    private const double DefaultThicknessM = 0.15;

    /// <summary>Asigna material y espesor a líneas o polilíneas y las pasa a la capa de muros.</summary>
    [CommandMethod("WIFIWALL")]
    public void AssignWall()
    {
        Document doc = CoreApp.DocumentManager.MdiActiveDocument;
        Database db = doc.Database;
        Editor ed = doc.Editor;
        MaterialCatalog catalog = MaterialCatalogFile.Catalog;

        var selectionOptions = new PromptSelectionOptions { MessageForAdding = "\nSeleccionar líneas o polilíneas de muro" };
        var filter = new SelectionFilter([new TypedValue((int)DxfCode.Start, "LINE,LWPOLYLINE")]);
        PromptSelectionResult selection = ed.GetSelection(selectionOptions, filter);
        if (selection.Status != PromptStatus.OK) return;

        string ids = string.Join("/", catalog.All.Select(m => m.Id));
        PromptResult materialAnswer = ed.GetString(new PromptStringOptions($"\nMaterial [{ids}]: ") { AllowSpaces = false });
        if (materialAnswer.Status != PromptStatus.OK) return;
        if (!catalog.TryGet(materialAnswer.StringResult.Trim(), out Material material))
        {
            ed.WriteMessage($"\nWIFIWALL: el material '{materialAnswer.StringResult}' no existe. Valores posibles: {ids}.\n");
            return;
        }

        var thicknessOptions = new PromptDoubleOptions("\nEspesor en metros")
        {
            AllowNegative = false,
            AllowZero = false,
            DefaultValue = DefaultThicknessM,
            UseDefaultValue = true,
        };
        PromptDoubleResult thicknessAnswer = ed.GetDouble(thicknessOptions);
        if (thicknessAnswer.Status != PromptStatus.OK) return;

        int count = 0;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            DrawingStore.EnsureAll(db, tr);
            foreach (ObjectId id in selection.Value.GetObjectIds())
            {
                var entity = (Entity)tr.GetObject(id, OpenMode.ForWrite);
                WifiXData.WriteWall(entity, material.Id, thicknessAnswer.Value);
                entity.Layer = WifiHeatNames.WallLayer;
                count++;
            }

            tr.Commit();
        }

        ed.WriteMessage(string.Create(CultureInfo.GetCultureInfo("es-AR"),
            $"\nWIFIWALL: {count} muro(s) con material {material.Name} ({material.Id}) y espesor {thicknessAnswer.Value:0.###} m.\n"));
    }
}
