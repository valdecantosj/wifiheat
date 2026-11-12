using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.Commands;

public sealed class ExportCommands
{
    /// <summary>Exporta el último cálculo a una imagen PNG.</summary>
    [CommandMethod("WIFIEXPORT")]
    public void Export() => CommandGuard.Run("WIFIEXPORT", doc =>
    {
        Editor ed = doc.Editor;
        AutoCadWifiHeatService service = Sessions.Service(doc);
        if (service.Session.LastGrid is null || service.Session.AccessPoint is null)
        {
            ed.WriteMessage("\nWIFIEXPORT: no hay un cálculo vigente. Ejecutar WIFICALCULATE.\n");
            return;
        }

        string suggested = service.DefaultExportPath();
        var options = new PromptStringOptions("\nArchivo PNG")
        {
            AllowSpaces = true,
            DefaultValue = suggested,
            UseDefaultValue = true,
        };
        PromptResult answer = ed.GetString(options);
        if (answer.Status != PromptStatus.OK) { CommandGuard.Cancelled(ed, "WIFIEXPORT"); return; }

        string path = string.IsNullOrWhiteSpace(answer.StringResult) ? suggested : answer.StringResult.Trim().Trim('"');
        try
        {
            path = Path.GetFullPath(path, Path.GetDirectoryName(suggested)!);
        }
        catch (System.Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            ed.WriteMessage($"\nWIFIEXPORT: la ruta '{path}' no es válida.\n");
            return;
        }

        OperationResult<string> result = service.ExportPng(path);
        CommandOutput.Problems(ed, "WIFIEXPORT", result.Errors, result.Warnings);
        if (result.Succeeded) ed.WriteMessage($"\nWIFIEXPORT: imagen guardada en {result.Value}\n");
    });
}
