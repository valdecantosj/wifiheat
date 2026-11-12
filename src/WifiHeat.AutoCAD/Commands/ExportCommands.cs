using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.AutoCAD.Export;
using WifiHeat.Core.Workflow;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD.Commands;

public sealed class ExportCommands
{
    /// <summary>Exporta el último cálculo a una imagen PNG.</summary>
    [CommandMethod("WIFIEXPORT")]
    public void Export()
    {
        Document doc = CoreApp.DocumentManager.MdiActiveDocument;
        Editor ed = doc.Editor;
        SimulationSession session = Sessions.For(doc);
        if (session.LastGrid is null || session.AccessPoint is null)
        {
            ed.WriteMessage("\nWIFIEXPORT: no hay un cálculo vigente. Ejecutar WIFICALCULATE.\n");
            return;
        }

        string suggested = DefaultPath(doc);
        var options = new PromptStringOptions("\nArchivo PNG")
        {
            AllowSpaces = true,
            DefaultValue = suggested,
            UseDefaultValue = true,
        };
        PromptResult answer = ed.GetString(options);
        if (answer.Status != PromptStatus.OK) return;

        string path = string.IsNullOrWhiteSpace(answer.StringResult) ? suggested : answer.StringResult.Trim().Trim('"');
        if (!Path.HasExtension(path)) path += ".png";
        path = Path.GetFullPath(path, Path.GetDirectoryName(suggested)!);

        try
        {
            PngComposer.Save(path, session.Walls, session.Heatmap()!, session.AccessPoint, Legend.For(session.Settings), session.OverlayFadePercent);
            ed.WriteMessage($"\nWIFIEXPORT: imagen guardada en {path}\n");
        }
        catch (System.Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            ed.WriteMessage($"\nWIFIEXPORT: no se pudo guardar {path}: {ex.Message}\n");
        }
    }

    private static string DefaultPath(Document doc)
    {
        bool saved = Convert.ToInt32(CoreApp.GetSystemVariable("DWGTITLED")) != 0;
        if (saved)
        {
            string dwg = doc.Database.Filename;
            return Path.Combine(Path.GetDirectoryName(dwg)!, Path.GetFileNameWithoutExtension(dwg) + "-wifiheat.png");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "wifiheat.png");
    }
}
