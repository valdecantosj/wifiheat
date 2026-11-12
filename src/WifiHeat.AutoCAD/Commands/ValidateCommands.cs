using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.AutoCAD.Reading;
using WifiHeat.Core.Workflow;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD.Commands;

public sealed class ValidateCommands
{
    /// <summary>Lee los muros del dibujo, los valida e informa cada problema con el handle de la entidad.</summary>
    [CommandMethod("WIFIVALIDATE")]
    public void Validate()
    {
        Document doc = CoreApp.DocumentManager.MdiActiveDocument;
        Editor ed = doc.Editor;

        ValidationReport report = LoadWalls(doc);
        Print(ed, "WIFIVALIDATE", Sessions.For(doc), report);
    }

    public static ValidationReport LoadWalls(Document doc)
    {
        DrawingWalls drawing = WallReader.Read(doc.Database);
        return Sessions.For(doc).LoadWalls(drawing.Walls, drawing.Issues);
    }

    public static void Print(Editor ed, string command, SimulationSession session, ValidationReport report)
    {
        ed.WriteMessage($"\n{command}: {session.Walls.Count} muro(s), {report.ErrorCount} error(es), {report.WarningCount} aviso(s).");
        foreach (ValidationIssue issue in report.Issues)
        {
            ed.WriteMessage($"\n  {issue}");
        }

        ed.WriteMessage("\n");
    }
}
