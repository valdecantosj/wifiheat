using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.Commands;

public sealed class QueryCommands
{
    /// <summary>Explica el RSSI estimado en el punto exacto indicado.</summary>
    [CommandMethod("WIFIQUERY")]
    public void Query() => CommandGuard.Run("WIFIQUERY", doc =>
    {
        Editor ed = doc.Editor;
        if (!DrawingContext.TryGetUnit(doc, "WIFIQUERY", out DrawingUnit unit)) return;
        if (!DrawingContext.TryGetPointM(ed, unit, "\nPunto a consultar: ", out Vec2 point)) { CommandGuard.Cancelled(ed, "WIFIQUERY"); return; }

        OperationResult<QueryResult> result = Sessions.Service(doc).Query(point);
        CommandOutput.Problems(ed, "WIFIQUERY", result.Errors, result.Warnings);
        if (result.Succeeded) ed.WriteMessage($"\n{result.Value!.Explanation}\n");
    });
}
