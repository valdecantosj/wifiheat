using Autodesk.AutoCAD.ApplicationServices;
using WifiHeat.AutoCAD.Reading;
using WifiHeat.Core.Workflow;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD;

/// <summary>Una sesión de simulación por documento abierto; se libera al cerrarlo.</summary>
public static class Sessions
{
    private static readonly Dictionary<Document, SimulationSession> ByDocument = [];

    public static void Attach() => CoreApp.DocumentManager.DocumentToBeDestroyed += OnDocumentToBeDestroyed;

    public static void Detach()
    {
        CoreApp.DocumentManager.DocumentToBeDestroyed -= OnDocumentToBeDestroyed;
        ByDocument.Clear();
    }

    public static SimulationSession For(Document document)
    {
        if (!ByDocument.TryGetValue(document, out SimulationSession? session))
        {
            session = new SimulationSession(MaterialCatalogFile.Catalog);
            RestoreAccessPoint(document, session);
            ByDocument[document] = session;
        }

        return session;
    }

    private static void RestoreAccessPoint(Document document, SimulationSession session)
    {
        StoredAccessPoint? stored = AccessPointReader.Read(document.Database);
        if (stored is null) return;

        session.PlaceAccessPoint(stored.Position);
        session.Configure(session.Settings with { Band = stored.Band, TxPowerDbm = stored.TxPowerDbm }, session.CellSizeM);
    }

    private static void OnDocumentToBeDestroyed(object? sender, DocumentCollectionEventArgs e) => ByDocument.Remove(e.Document);
}
