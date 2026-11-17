using System.Globalization;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.Commands;

public sealed class CalculateCommands
{
    private const string ResolutionKeyword = "Resolucion";
    private const string FadeKeyword = "Atenuacion";
    private const string CalculateKeyword = "Calcular";
    private const string ModelKeyword = "Modelo";
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    /// <summary>Valida, calcula la grilla y dibuja el mapa de calor y la leyenda.</summary>
    [CommandMethod("WIFICALCULATE")]
    public void Calculate() => CommandGuard.Run("WIFICALCULATE", doc =>
    {
        Editor ed = doc.Editor;
        AutoCadWifiHeatService service = Sessions.Service(doc);
        if (!AskOptions(ed, service)) { CommandGuard.Cancelled(ed, "WIFICALCULATE"); return; }

        OperationResult<CalculationSummary> result = service.Calculate();
        if (!result.Succeeded)
        {
            CommandOutput.Problems(ed, "WIFICALCULATE", result.Errors, result.Warnings);
            return;
        }

        CalculationSummary s = result.Value!;
        foreach (string warning in s.Warnings) ed.WriteMessage($"\n  [AVISO] {warning}");
        ed.WriteMessage(string.Create(Es,
            $"\nWIFICALCULATE: {s.Cells} celdas ({s.Columns} × {s.Rows}, {s.CellSizeM:0.##} m) · RSSI mín {s.MinDbm:0.0} dBm · máx {s.MaxDbm:0.0} dBm · cálculo {s.CalculationMs:0} ms · {(s.ImagePath is null ? 0 : 1)} entidad(es) de overlay.\n"));
        ed.WriteMessage(string.Create(Es,
            $"  Imagen: {s.ImageWidthPx} × {s.ImageHeightPx} px de {s.PixelSizeM * 100:0.##} cm · atenuación {s.OverlayFadePercent} % · {s.ImageMs:0} ms · {s.ImagePath ?? "sin imagen"}\n"));
        ed.WriteMessage($"  Área de cobertura: {s.AreaDescription} ({s.CellsWithValue} celdas con valor).\n");
        ed.WriteMessage($"  Modelo de propagación: {PropagationModelInfo.Label(s.Model)}.\n");
    });

    /// <summary>
    /// Enter calcula con los valores vigentes. Resolución, Atenuación y Modelo cambian la configuración de la sesión y vuelven a preguntar;
    /// Atenuación se aplica además al mapa ya dibujado.
    /// Devuelve false si el usuario cancela.
    /// </summary>
    private static bool AskOptions(Editor ed, IWifiHeatService service)
    {
        while (true)
        {
            SimulationSettings current = service.Settings;
            var options = new PromptKeywordOptions(string.Create(Es,
                $"\nResolución {current.CellSizeM:0.00} m · atenuación {current.OverlayFadePercent} % · modelo {ModelText(current)}. Elegir opción")) { AllowNone = true };
            options.Keywords.Add(CalculateKeyword);
            options.Keywords.Add(ResolutionKeyword, ResolutionKeyword, "Resolución");
            options.Keywords.Add(FadeKeyword, FadeKeyword, "Atenuación");
            options.Keywords.Add(ModelKeyword);
            options.Keywords.Default = CalculateKeyword;
            PromptResult answer = ed.GetKeywords(options);
            if (answer.Status == PromptStatus.None || (answer.Status == PromptStatus.OK && answer.StringResult == CalculateKeyword)) return true;
            if (answer.Status != PromptStatus.OK) return false;

            SimulationSettings changed;
            if (answer.StringResult == ResolutionKeyword)
            {
                PromptDoubleResult size = ed.GetDouble(new PromptDoubleOptions(string.Create(Es,
                    $"\nTamaño de celda en metros ({SimulationSession.MinCellSizeM:0.00} a {SimulationSession.MaxCellSizeM:0})"))
                {
                    DefaultValue = current.CellSizeM,
                    UseDefaultValue = true,
                    AllowNegative = false,
                    AllowZero = false,
                });
                if (size.Status != PromptStatus.OK) return false;
                changed = current with { CellSizeM = size.Value };
            }
            else if (answer.StringResult == ModelKeyword)
            {
                if (AskModel(ed, service, current) is not { } withModel) return false;
                changed = withModel;
            }
            else
            {
                PromptIntegerResult fade = ed.GetInteger(new PromptIntegerOptions(
                    $"\nAtenuación del mapa en % ({OverlayFade.MinPercent} = colores plenos, máximo {OverlayFade.MaxPercent})")
                {
                    DefaultValue = current.OverlayFadePercent,
                    UseDefaultValue = true,
                    AllowNegative = false,
                });
                if (fade.Status != PromptStatus.OK) return false;
                OperationResult<int> faded = service.SetOverlayFade(fade.Value);
                CommandOutput.Problems(ed, "WIFICALCULATE", faded.Errors, faded.Warnings);
                continue;
            }

            OperationResult<SimulationSettings> configured = service.Configure(changed);
            CommandOutput.Problems(ed, "WIFICALCULATE", configured.Errors, configured.Warnings);
        }
    }

    private static readonly (PropagationModel Model, string Keyword, string Display)[] ModelKeywords =
    [
        (PropagationModel.StraightLine, "Recta", "Recta"),
        (PropagationModel.Diffraction, "Difraccion", "Difracción"),
        (PropagationModel.DiffractionAndReflection, "Reflexiones", "Reflexiones"),
    ];

    private static string ModelText(SimulationSettings settings) => settings.Model == PropagationModel.StraightLine
        ? PropagationModelInfo.Label(settings.Model)
        : $"{PropagationModelInfo.Label(settings.Model)} (hasta {settings.MaxDiffractions} difracciones)";

    /// <summary>Pide el modelo entre los disponibles y, si usa difracción, el tope de difracciones por trayecto. Null si el usuario cancela.</summary>
    private static SimulationSettings? AskModel(Editor ed, IWifiHeatService service, SimulationSettings current)
    {
        var options = new PromptKeywordOptions("\nModelo") { AllowNone = true };
        foreach ((PropagationModel model, string keyword, string display) in ModelKeywords)
        {
            if (!service.PropagationModels.Contains(model)) continue;
            options.Keywords.Add(keyword, keyword, display);
            if (model == current.Model) options.Keywords.Default = keyword;
        }

        PromptResult answer = ed.GetKeywords(options);
        if (answer.Status != PromptStatus.OK && answer.Status != PromptStatus.None) return null;

        PropagationModel chosen = answer.Status == PromptStatus.None
            ? current.Model
            : ModelKeywords.First(k => k.Keyword == answer.StringResult).Model;
        if (chosen == PropagationModel.StraightLine) return current with { Model = chosen };

        PromptIntegerResult limit = ed.GetInteger(new PromptIntegerOptions(
            $"\nDifracciones máximas por trayecto [{PropagationOptions.MinDiffractions}-{PropagationOptions.MaxDiffractionsLimit}]")
        {
            DefaultValue = current.MaxDiffractions,
            UseDefaultValue = true,
            AllowNegative = false,
            AllowZero = false,
            LowerLimit = PropagationOptions.MinDiffractions,
            UpperLimit = PropagationOptions.MaxDiffractionsLimit,
        });
        return limit.Status == PromptStatus.OK ? current with { Model = chosen, MaxDiffractions = limit.Value } : null;
    }
}
