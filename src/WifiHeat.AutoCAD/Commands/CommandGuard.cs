using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD.Commands;

/// <summary>Ejecuta un comando sobre el documento activo sin dejar escapar excepciones a AutoCAD.</summary>
public static class CommandGuard
{
    public static void Run(string command, Action<Document> body)
    {
        Document? doc = CoreApp.DocumentManager.MdiActiveDocument;
        if (doc is null) return;

        try
        {
            body(doc);
        }
        catch (System.Exception ex)
        {
            doc.Editor.WriteMessage($"\n{command}: el comando se interrumpió por un error inesperado ({ex.GetType().Name}: {ex.Message}). Los cambios de este paso no se guardaron.\n");
        }
    }

    public static void Cancelled(Editor ed, string command) => ed.WriteMessage($"\n{command}: cancelado.\n");
}
