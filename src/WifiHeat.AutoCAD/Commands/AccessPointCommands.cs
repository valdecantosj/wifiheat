using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using WifiHeat.AutoCAD.Rendering;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Workflow;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD.Commands;

public sealed class AccessPointCommands
{
    private const string Keyword2_4 = "2.4";
    private const string Keyword5 = "5";

    /// <summary>Ubica el AP, pide banda y potencia de transmisión y dibuja el marcador.</summary>
    [CommandMethod("WIFIADDAP")]
    public void AddAccessPoint()
    {
        Document doc = CoreApp.DocumentManager.MdiActiveDocument;
        Database db = doc.Database;
        Editor ed = doc.Editor;
        if (!DrawingContext.TryGetUnit(doc, "WIFIADDAP", out DrawingUnit unit)) return;
        if (!DrawingContext.TryGetPointM(ed, unit, "\nUbicación del AP: ", out Vec2 position)) return;

        SimulationSession session = Sessions.For(doc);
        session.PlaceAccessPoint(position);

        RadioSettings settings = session.Settings;
        var bandOptions = new PromptKeywordOptions("\nBanda en GHz") { AllowNone = true };
        bandOptions.Keywords.Add(Keyword2_4);
        bandOptions.Keywords.Add(Keyword5);
        bandOptions.Keywords.Default = settings.Band == Band.Ghz5 ? Keyword5 : Keyword2_4;
        PromptResult bandAnswer = ed.GetKeywords(bandOptions);
        if (bandAnswer.Status == PromptStatus.OK)
        {
            settings = settings with { Band = bandAnswer.StringResult == Keyword5 ? Band.Ghz5 : Band.Ghz2_4 };
        }

        var txOptions = new PromptDoubleOptions("\nPotencia de transmisión en dBm")
        {
            DefaultValue = settings.TxPowerDbm,
            UseDefaultValue = true,
        };
        PromptDoubleResult txAnswer = ed.GetDouble(txOptions);
        if (txAnswer.Status == PromptStatus.OK)
        {
            settings = settings with { TxPowerDbm = txAnswer.Value };
        }

        OperationResult<RadioSettings> configured = session.Configure(settings, session.CellSizeM);
        foreach (string error in configured.Errors)
        {
            ed.WriteMessage($"\nWIFIADDAP: {error} Se mantiene el valor anterior.");
        }

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            AccessPointMarker.Draw(db, tr, unit, session.AccessPoint!, session.Settings);
            tr.Commit();
        }

        RadioSettings s = session.Settings;
        ed.WriteMessage(string.Create(CultureInfo.GetCultureInfo("es-AR"),
            $"\nWIFIADDAP: {session.AccessPoint!.Name} en {position} m · Banda {BandInfo.Label(s.Band)} ({s.FrequencyMHz:0} MHz) · P_TX = {s.TxPowerDbm:0.#} dBm.\n"));
    }
}
