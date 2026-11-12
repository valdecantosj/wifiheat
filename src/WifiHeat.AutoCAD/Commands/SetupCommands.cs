using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.Commands;

public sealed class SetupCommands
{
    private const string Metres = "Metros";
    private const string Centimetres = "Centimetros";
    private const string Millimetres = "Milimetros";

    /// <summary>Crea capas y aplicación registrada, y guarda la unidad del dibujo.</summary>
    [CommandMethod("WIFISETUP")]
    public void Setup() => CommandGuard.Run("WIFISETUP", doc =>
    {
        Editor ed = doc.Editor;
        AutoCadWifiHeatService service = Sessions.Service(doc);
        DrawingSetup setup = service.GetDrawingSetup();
        DrawingUnit suggested = setup.Unit ?? setup.SuggestedUnit;

        var options = new PromptKeywordOptions($"\nUnidad del dibujo ({setup.DeclaredUnits})") { AllowNone = true };
        options.Keywords.Add(Metres);
        options.Keywords.Add(Centimetres, Centimetres, "Centímetros");
        options.Keywords.Add(Millimetres, Millimetres, "Milímetros");
        options.Keywords.Default = Keyword(suggested);
        PromptResult answer = ed.GetKeywords(options);
        if (answer.Status is not (PromptStatus.OK or PromptStatus.None)) { CommandGuard.Cancelled(ed, "WIFISETUP"); return; }

        DrawingUnit unit = answer.Status == PromptStatus.None ? suggested : answer.StringResult switch
        {
            Centimetres => DrawingUnit.Centimetres,
            Millimetres => DrawingUnit.Millimetres,
            _ => DrawingUnit.Metres,
        };

        OperationResult<DrawingUnit> result = service.SetDrawingUnit(unit);
        CommandOutput.Problems(ed, "WIFISETUP", result.Errors, result.Warnings);
        if (result.Succeeded) ed.WriteMessage($"\nWIFISETUP: capas y aplicación {WifiHeatNames.AppName} listas. Unidad del dibujo: {DrawingUnits.Label(unit)}.\n");
    });

    private static string Keyword(DrawingUnit unit) => unit switch
    {
        DrawingUnit.Centimetres => Centimetres,
        DrawingUnit.Millimetres => Millimetres,
        _ => Metres,
    };
}
