using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD.Commands;

/// <summary>Comandos de diagnóstico: comprobar la carga e inspeccionar un DWG.</summary>
public sealed class DiagnosticCommands
{
    [CommandMethod("WIFI")]
    public void ShowHelp() => CommandGuard.Run("WIFI", doc =>
    {
        Editor ed = doc.Editor;
        ed.WriteMessage($"\nWifiHeat {WifiHeatApplication.Version} · .NET {Environment.Version}");
        ed.WriteMessage("\nFlujo: WIFISETUP, WIFIEJES (opcional, muros de doble cara), WIFIWALL, WIFIVALIDATE, WIFIADDAP, WIFIAREA (opcional), WIFICALCULATE, WIFIQUERY, WIFIEXPORT.");
        ed.WriteMessage("\nDiagnóstico: WIFIINFO.\n");
    });

    /// <summary>Lista unidades, extensión, capas y cantidad de entidades por capa y tipo en el espacio modelo.</summary>
    [CommandMethod("WIFIINFO")]
    public void DrawingInfo() => CommandGuard.Run("WIFIINFO", doc =>
    {
        Database db = doc.Database;
        Editor ed = doc.Editor;

        ed.WriteMessage($"\n== WIFIINFO {Path.GetFileName(db.Filename)} ==");
        ed.WriteMessage($"\nINSUNITS={db.Insunits} LUNITS={db.Lunits} MEASUREMENT={db.Measurement}");
        ed.WriteMessage($"\nEXTMIN={db.Extmin} EXTMAX={db.Extmax}");

        using Transaction tr = db.TransactionManager.StartTransaction();
        var layers = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        ed.WriteMessage("\n-- Capas --");
        foreach (ObjectId id in layers)
        {
            var layer = (LayerTableRecord)tr.GetObject(id, OpenMode.ForRead);
            ed.WriteMessage($"\nLAYER|{layer.Name}|off={layer.IsOff}|frozen={layer.IsFrozen}");
        }

        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        var counts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (ObjectId id in modelSpace)
        {
            var entity = (Entity)tr.GetObject(id, OpenMode.ForRead);
            string kind = entity is BlockReference br ? $"INSERT:{br.Name}" : entity.GetRXClass().DxfName;
            string key = $"{entity.Layer}|{kind}";
            counts[key] = counts.GetValueOrDefault(key) + 1;
        }

        ed.WriteMessage("\n-- Entidades en espacio modelo (capa|tipo|cantidad) --");
        foreach ((string key, int count) in counts)
        {
            ed.WriteMessage($"\nENT|{key}|{count}");
        }

        tr.Commit();
        ed.WriteMessage("\n== FIN WIFIINFO ==\n");
    });
}
