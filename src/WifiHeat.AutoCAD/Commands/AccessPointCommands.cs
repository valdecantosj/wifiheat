using System.Globalization;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Service;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.Commands;

public sealed class AccessPointCommands
{
    private const string Keyword2_4 = "2.4";
    private const string Keyword5 = "5";

    /// <summary>Ubica el AP, pide banda y potencia de transmisión y dibuja el marcador.</summary>
    [CommandMethod("WIFIADDAP")]
    public void AddAccessPoint() => CommandGuard.Run("WIFIADDAP", doc =>
    {
        Editor ed = doc.Editor;
        if (!DrawingContext.TryGetUnit(doc, "WIFIADDAP", out DrawingUnit unit)) return;
        if (!DrawingContext.TryGetPointM(ed, unit, "\nUbicación del AP: ", out Vec2 position)) { CommandGuard.Cancelled(ed, "WIFIADDAP"); return; }

        AutoCadWifiHeatService service = Sessions.Service(doc);
        SimulationSettings settings = service.Settings;

        var bandOptions = new PromptKeywordOptions("\nBanda en GHz") { AllowNone = true };
        bandOptions.Keywords.Add(Keyword2_4);
        bandOptions.Keywords.Add(Keyword5);
        bandOptions.Keywords.Default = settings.Band == Band.Ghz5 ? Keyword5 : Keyword2_4;
        PromptResult bandAnswer = ed.GetKeywords(bandOptions);
        if (bandAnswer.Status is not (PromptStatus.OK or PromptStatus.None)) { CommandGuard.Cancelled(ed, "WIFIADDAP"); return; }
        if (bandAnswer.Status == PromptStatus.OK) settings = settings with { Band = bandAnswer.StringResult == Keyword5 ? Band.Ghz5 : Band.Ghz2_4 };

        var txOptions = new PromptDoubleOptions("\nPotencia de transmisión en dBm (-10 a 40)")
        {
            DefaultValue = settings.TxPowerDbm,
            UseDefaultValue = true,
        };
        PromptDoubleResult txAnswer = ed.GetDouble(txOptions);
        if (txAnswer.Status is not (PromptStatus.OK or PromptStatus.None)) { CommandGuard.Cancelled(ed, "WIFIADDAP"); return; }
        if (txAnswer.Status == PromptStatus.OK) settings = settings with { TxPowerDbm = txAnswer.Value };

        OperationResult<SimulationSettings> configured = service.Configure(settings);
        foreach (string error in configured.Errors) ed.WriteMessage($"\nWIFIADDAP: {error} Se mantiene el valor anterior.");

        OperationResult<AccessPointInfo> placed = service.SetAccessPoint(position);
        CommandOutput.Problems(ed, "WIFIADDAP", placed.Errors, placed.Warnings);
        if (!placed.Succeeded) return;

        AccessPointInfo ap = placed.Value!;
        ed.WriteMessage(string.Create(CultureInfo.GetCultureInfo("es-AR"),
            $"\nWIFIADDAP: {ap.Name} en {ap.PositionM} m · Banda {BandInfo.Label(ap.Band)} ({ap.FrequencyMHz:0} MHz) · P_TX = {ap.TxPowerDbm:0.#} dBm.\n"));
    });
}
