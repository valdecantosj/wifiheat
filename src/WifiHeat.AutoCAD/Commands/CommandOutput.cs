using Autodesk.AutoCAD.EditorInput;

namespace WifiHeat.AutoCAD.Commands;

/// <summary>Formato común de errores y avisos en la línea de comandos.</summary>
public static class CommandOutput
{
    public static void Problems(Editor ed, string command, IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        foreach (string error in errors) ed.WriteMessage($"\n{command}: {error}");
        foreach (string warning in warnings) ed.WriteMessage($"\n  [AVISO] {warning}");
        if (errors.Count > 0) ed.WriteMessage("\n");
    }
}
