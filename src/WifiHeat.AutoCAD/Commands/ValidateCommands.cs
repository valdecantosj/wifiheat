using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.Core.Service;

namespace WifiHeat.AutoCAD.Commands;

public sealed class ValidateCommands
{
    /// <summary>Lee los muros del dibujo, los valida e informa cada problema con el handle de la entidad.</summary>
    [CommandMethod("WIFIVALIDATE")]
    public void Validate() => CommandGuard.Run("WIFIVALIDATE", doc =>
        Print(doc.Editor, "WIFIVALIDATE", Sessions.Service(doc).Validate()));

    public static void Print(Editor ed, string command, ValidationSummary summary)
    {
        ed.WriteMessage($"\n{command}: {summary.WallCount} muro(s), {summary.ErrorCount} error(es), {summary.WarningCount} aviso(s).");
        if (summary.WallBounds is { } bounds) ed.WriteMessage($"\n  Envolvente de los muros: {bounds.Min} a {bounds.Max} m.");
        foreach (ValidationItem item in summary.Items) ed.WriteMessage($"\n  {item}");
        ed.WriteMessage("\n");
    }
}
