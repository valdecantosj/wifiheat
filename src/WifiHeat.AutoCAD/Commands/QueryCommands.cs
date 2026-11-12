using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD.Commands;

public sealed class QueryCommands
{
    /// <summary>Explica el RSSI estimado en el punto exacto indicado.</summary>
    [CommandMethod("WIFIQUERY")]
    public void Query()
    {
        Document doc = CoreApp.DocumentManager.MdiActiveDocument;
        Editor ed = doc.Editor;
        if (!DrawingContext.TryGetUnit(doc, "WIFIQUERY", out DrawingUnit unit)) return;

        SimulationSession session = Sessions.For(doc);
        if (session.Validation is null) ValidateCommands.LoadWalls(doc);
        if (!DrawingContext.TryGetPointM(ed, unit, "\nPunto a consultar: ", out Vec2 point)) return;

        OperationResult<PointCalculation> result = session.Query(point);
        if (!result.Succeeded)
        {
            foreach (string error in result.Errors) ed.WriteMessage($"\nWIFIQUERY: {error}");
            ed.WriteMessage("\n");
            return;
        }

        ed.WriteMessage($"\n{Explanation.Format(result.Value!)}\n");
    }
}
