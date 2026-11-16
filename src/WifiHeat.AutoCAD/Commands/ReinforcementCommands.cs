using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.Core.Service;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.Commands;

public sealed class ReinforcementCommands
{
    /// <summary>Reemplaza el mapa por la pérdida en dB que agrega la armadura de las columnas. WIFICALCULATE vuelve al mapa de cobertura.</summary>
    [CommandMethod("WIFIARMADURA")]
    public void Show() => CommandGuard.Run("WIFIARMADURA", doc =>
    {
        Editor ed = doc.Editor;
        OperationResult<ReinforcementEffectSummary> result = Sessions.Service(doc).ShowReinforcementEffect();
        if (!result.Succeeded)
        {
            CommandOutput.Problems(ed, "WIFIARMADURA", result.Errors, result.Warnings);
            return;
        }

        ReinforcementEffectSummary s = result.Value!;
        foreach (string warning in s.Warnings) ed.WriteMessage($"\n  [AVISO] {warning}");
        ed.WriteMessage($"\nWIFIARMADURA: {s.Headline}.");
        foreach (string line in s.Details) ed.WriteMessage($"\n  {line}");
        if (!s.NothingToShow) ed.WriteMessage($"\n  Imagen: {s.ImagePath ?? "sin imagen"}");
        ed.WriteMessage("\n");
    });
}
