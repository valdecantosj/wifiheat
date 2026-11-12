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
    });

    /// <summary>
    /// Enter calcula con los valores vigentes. Resolución y Atenuación cambian la configuración de la sesión y vuelven a preguntar;
    /// Atenuación se aplica además al mapa ya dibujado.
    /// Devuelve false si el usuario cancela.
    /// </summary>
    private static bool AskOptions(Editor ed, IWifiHeatService service)
    {
        while (true)
        {
            SimulationSettings current = service.Settings;
            var options = new PromptKeywordOptions(string.Create(Es,
                $"\nResolución {current.CellSizeM:0.00} m · atenuación {current.OverlayFadePercent} %. Elegir opción")) { AllowNone = true };
            options.Keywords.Add(CalculateKeyword);
            options.Keywords.Add(ResolutionKeyword, ResolutionKeyword, "Resolución");
            options.Keywords.Add(FadeKeyword, FadeKeyword, "Atenuación");
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
}
