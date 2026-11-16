using System.Globalization;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.Core.Service;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.Commands;

public sealed class ValidateCommands
{
    private const string Command = "WIFIVALIDATE";
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>
    /// Lee los muros del dibujo, los valida e informa cada problema con el handle de la entidad.
    /// Si hay muros paralelos que suman dos veces, ofrece convertirlos en un eje.
    /// </summary>
    [CommandMethod(Command)]
    public void Validate() => CommandGuard.Run(Command, doc =>
    {
        Editor ed = doc.Editor;
        AutoCadWifiHeatService service = Sessions.Service(doc);
        Print(ed, Command, service.Validate());

        IReadOnlyList<ParallelWallsInfo> parallel = service.FindParallelWalls();
        if (parallel.Count == 0) return;

        string question = $"\nHay {parallel.Count} grupo(s) de muros paralelos a menos de 0,50 m. Convertirlos en ejes";
        if (!CommandOutput.AskYesNo(ed, question, defaultYes: false, out bool convert))
        {
            CommandGuard.Cancelled(ed, Command);
            return;
        }

        if (!convert) return;

        OperationResult<ParallelConversion> converted = service.ConvertParallelWalls();
        CommandOutput.Problems(ed, Command, converted.Errors, converted.Warnings);
        if (!converted.Succeeded) return;

        ParallelConversion c = converted.Value!;
        ed.WriteMessage(string.Create(Es,
            $"\n{Command}: {c.AxesCreated} eje(s) creado(s); {c.FacesMoved} cara(s) pasaron a la capa {WifiHeatNames.FaceLayer}, que no se lee como muro."));
        Print(ed, Command, service.Validate());
    });

    public static void Print(Editor ed, string command, ValidationSummary summary)
    {
        string columns = summary.ColumnCount == 0 ? "" : $", {summary.ColumnCount} columna(s)";
        ed.WriteMessage($"\n{command}: {summary.WallCount - summary.ColumnCount} muro(s){columns}, {summary.ErrorCount} error(es), {summary.WarningCount} aviso(s).");
        if (summary.WallBounds is { } bounds) ed.WriteMessage($"\n  Envolvente de los muros: {bounds.Min} a {bounds.Max} m.");
        foreach (ValidationItem item in summary.Items) ed.WriteMessage($"\n  {item}");
        ed.WriteMessage("\n");
    }
}
