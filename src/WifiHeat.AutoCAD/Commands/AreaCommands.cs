using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.Commands;

public sealed class AreaCommands
{
    private const string RectangleKeyword = "Rectangulo";
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>Define el área de cobertura con una polilínea cerrada, o vuelve al rectángulo de los muros.</summary>
    [CommandMethod("WIFIAREA")]
    public void DefineArea() => CommandGuard.Run("WIFIAREA", doc =>
    {
        Editor ed = doc.Editor;
        if (!DrawingContext.TryGetUnit(doc, "WIFIAREA", out DrawingUnit _)) return;

        var options = new PromptEntityOptions("\nSeleccionar polilínea cerrada del área de cobertura") { AppendKeywordsToMessage = true };
        options.Keywords.Add(RectangleKeyword, RectangleKeyword, "Rectángulo");
        options.SetRejectMessage("\nDebe ser una polilínea.");
        options.AddAllowedClass(typeof(Polyline), true);
        PromptEntityResult answer = ed.GetEntity(options);
        if (answer.Status is not (PromptStatus.OK or PromptStatus.Keyword)) { CommandGuard.Cancelled(ed, "WIFIAREA"); return; }

        AutoCadWifiHeatService service = Sessions.Service(doc);
        OperationResult<CoverageAreaInfo> result = answer.Status == PromptStatus.Keyword
            ? service.ClearCoverageArea()
            : service.SetCoverageArea(answer.ObjectId.Handle.ToString());
        CommandOutput.Problems(ed, "WIFIAREA", result.Errors, result.Warnings);
        if (!result.Succeeded) return;

        CoverageAreaInfo area = result.Value!;
        ed.WriteMessage(area.IsCustom
            ? string.Create(Es, $"\nWIFIAREA: área de cobertura = {area.Description}, {area.VertexCount} vértices, {area.AreaM2:0.##} m², de {area.Bounds!.Value.Min} a {area.Bounds.Value.Max} m.\n")
            : "\nWIFIAREA: se usa el rectángulo de los muros.\n");
    });
}
