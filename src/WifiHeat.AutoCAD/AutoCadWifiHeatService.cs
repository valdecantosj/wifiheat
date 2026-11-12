using System.Globalization;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using WifiHeat.AutoCAD.Export;
using WifiHeat.AutoCAD.Reading;
using WifiHeat.AutoCAD.Rendering;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD;

/// <summary>
/// <see cref="IWifiHeatService"/> sobre un documento de AutoCAD. Comandos y panel llaman a esta misma instancia.
/// Usar solo desde el hilo de AutoCAD; cada escritura bloquea el documento, así que sirve también desde una ventana no modal.
/// </summary>
public sealed class AutoCadWifiHeatService(Document document, SimulationSession session) : WifiHeatService(session)
{
    public Document Document { get; } = document;

    private Database Db => Document.Database;

    protected override DrawingContents ReadDrawing()
    {
        DrawingWalls walls = WallReader.Read(Db);
        return new DrawingContents([.. walls.Walls.Select(w => new DrawingWall(w, WifiHeatNames.WallLayer))], walls.Issues);
    }

    protected override CoverageAreaRead ReadCoverageArea()
    {
        const string rectangle = "rectángulo de los muros";
        DrawingUnit? unit = DrawingStore.ReadUnit(Db);
        if (unit is null) return new CoverageAreaRead(null, rectangle, null);

        StoredArea area = AreaReader.Read(Db, unit.Value);
        return new CoverageAreaRead(area.Polygon, area.Polygon is null ? rectangle : $"polilínea {area.Handle}", area.Warning);
    }

    protected override IReadOnlyList<string> WriteWalls(IReadOnlyList<WallAssignment> assignments)
    {
        var missing = new List<string>();
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        DrawingStore.EnsureAll(Db, tr);
        foreach (WallAssignment a in assignments)
        {
            if (!TryOpenCurve(tr, a.Handle, out Entity? entity))
            {
                missing.Add(a.Handle);
                continue;
            }

            entity!.UpgradeOpen();
            WifiXData.WriteWall(entity, a.MaterialId, a.ThicknessM);
            entity.Layer = WifiHeatNames.WallLayer;
        }

        tr.Commit();
        return missing;
    }

    protected override string? SaveAccessPoint(AccessPoint ap, RadioSettings settings)
    {
        DrawingUnit? unit = DrawingStore.ReadUnit(Db);
        if (unit is null) return "El dibujo no tiene unidad guardada; el AP no se dibujó. Ejecutar WIFISETUP.";

        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        AccessPointMarker.Draw(Db, tr, unit.Value, ap, settings);
        tr.Commit();
        return null;
    }

    protected override OverlayOutcome DrawResult(RssiGrid grid, HeatmapImage image, Legend legend, int fadePercent)
    {
        DrawingUnit unit = DrawingStore.ReadUnit(Db) ?? DrawingUnit.Metres;
        (string folder, string baseName, bool saved) = OverlayImageLocation();
        OverlayResult overlay;
        using (Document.LockDocument())
        using (Transaction tr = Db.TransactionManager.StartTransaction())
        {
            DrawingStore.EnsureAll(Db, tr);
            overlay = RasterOverlayRenderer.Render(Db, tr, unit, image, folder, baseName, fadePercent);
            LegendRenderer.Render(Db, tr, unit, grid.Spec, legend, fadePercent);
            tr.Commit();
        }

        var warnings = new List<string>(overlay.Warnings);
        if (!saved) warnings.Add("El dibujo no está guardado: la imagen del mapa se guardó en la carpeta temporal. Guardar el dibujo y volver a calcular para dejarla junto al DWG.");
        return new OverlayOutcome(overlay.ImagePath, warnings);
    }

    protected override void WritePng(string path, IReadOnlyList<Wall> walls, HeatmapImage image, AccessPoint ap, Legend legend, int fadePercent) =>
        PngComposer.Save(path, walls, image, ap, legend, fadePercent);

    /// <summary>Ruta sugerida para WIFIEXPORT: junto al DWG o en Documentos si el dibujo no está guardado.</summary>
    public string DefaultExportPath()
    {
        if (IsSaved()) return Path.Combine(Path.GetDirectoryName(Db.Filename)!, Path.GetFileNameWithoutExtension(Db.Filename) + "-wifiheat.png");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "wifiheat.png");
    }

    private (string Folder, string BaseName, bool NextToDrawing) OverlayImageLocation() => IsSaved()
        ? (Path.GetDirectoryName(Db.Filename)!, Path.GetFileNameWithoutExtension(Db.Filename), true)
        : (Path.GetTempPath(), Path.GetFileNameWithoutExtension(Document.Name), false);

    private bool IsSaved() =>
        Document == CoreApp.DocumentManager.MdiActiveDocument
            ? Convert.ToInt32(CoreApp.GetSystemVariable("DWGTITLED")) != 0
            : File.Exists(Db.Filename);

    private bool TryOpenCurve(Transaction tr, string handleText, out Entity? entity)
    {
        entity = null;
        if (!long.TryParse(handleText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value)) return false;
        if (!Db.TryGetObjectId(new Handle(value), out ObjectId id) || id.IsErased) return false;

        entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
        return entity is Line or Polyline;
    }
}
