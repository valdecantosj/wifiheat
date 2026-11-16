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
/// Usar solo desde el hilo de AutoCAD; cada lectura y escritura bloquea el documento, así que sirve también desde una paleta no modal.
/// </summary>
public sealed class AutoCadWifiHeatService(Document document, SimulationSession session) : WifiHeatService(session)
{
    private const string RectangleDescription = "rectángulo de los muros";

    public Document Document { get; } = document;

    private Database Db => Document.Database;

    public override string SuggestedExportPath => DefaultExportPath();

    protected override DrawingSetup ReadSetup()
    {
        using DocumentLock _ = Document.LockDocument();
        return new DrawingSetup(DrawingStore.ReadUnit(Db), DrawingStore.SuggestUnit(Db), $"INSUNITS={Db.Insunits}");
    }

    protected override void WriteUnit(DrawingUnit unit)
    {
        using DocumentLock _ = Document.LockDocument();
        using (Transaction tr = Db.TransactionManager.StartTransaction())
        {
            DrawingStore.EnsureAll(Db, tr);
            DrawingStore.WriteUnit(Db, tr, unit);
            tr.Commit();
        }

        StoredAccessPoint? stored = AccessPointReader.Read(Db);
        if (stored is not null && stored.Position != Session.AccessPoint?.Position) Session.PlaceAccessPoint(stored.Position);
    }

    protected override DrawingContents ReadDrawing()
    {
        using DocumentLock _ = Document.LockDocument();
        DrawingWalls walls = WallReader.Read(Db);
        return new DrawingContents([.. walls.Walls.Select(w => new DrawingWall(w, WifiHeatNames.WallLayer))], walls.Issues);
    }

    protected override CoverageAreaRead ReadCoverageArea()
    {
        using DocumentLock _ = Document.LockDocument();
        DrawingUnit? unit = DrawingStore.ReadUnit(Db);
        if (unit is null) return new CoverageAreaRead(null, RectangleDescription, null);

        StoredArea area = AreaReader.Read(Db, unit.Value);
        return new CoverageAreaRead(area.Polygon, area.Polygon is null ? RectangleDescription : $"polilínea {area.Handle}", area.Warning, area.Polygon is null ? null : area.Handle);
    }

    protected override OperationResult<CoverageAreaRead> WriteCoverageArea(string? polylineHandle)
    {
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        DrawingUnit unit = DrawingStore.ReadUnit(Db, tr) ?? DrawingUnit.Metres;
        if (polylineHandle is null)
        {
            DrawingStore.WriteAreaId(Db, tr, null);
            tr.Commit();
            return OperationResult<CoverageAreaRead>.Ok(new CoverageAreaRead(null, RectangleDescription, null));
        }

        if (!TryOpen(tr, polylineHandle, out Entity? entity) || entity is not Polyline polyline)
        {
            return OperationResult<CoverageAreaRead>.Fail($"La entidad {polylineHandle} no es una polilínea. No se cambió el área.");
        }

        if (!AreaReader.TryPolygon(polyline, unit, out Polygon2? polygon, out string? error))
        {
            return OperationResult<CoverageAreaRead>.Fail($"La polilínea {polyline.Handle} {error}. No se cambió el área.");
        }

        DrawingStore.WriteAreaId(Db, tr, polyline.ObjectId);
        tr.Commit();
        string handle = polyline.Handle.ToString();
        return OperationResult<CoverageAreaRead>.Ok(new CoverageAreaRead(polygon, $"polilínea {handle}", null, handle));
    }

    protected override IReadOnlyList<LayerFaces> ReadFaces(BoundingBox windowM)
    {
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        IReadOnlyList<LayerFaces> faces = FaceReader.ReadFaces(Db, tr, DrawingStore.ReadUnit(Db, tr) ?? DrawingUnit.Metres, windowM);
        tr.Commit();
        return faces;
    }

    protected override IReadOnlyList<GeneratedAxis> ReadGeneratedAxes(BoundingBox windowM)
    {
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        IReadOnlyList<GeneratedAxis> axes = FaceReader.ReadGeneratedAxes(Db, tr, DrawingStore.ReadUnit(Db, tr) ?? DrawingUnit.Metres, windowM);
        tr.Commit();
        return axes;
    }

    protected override void ReplaceAxes(IReadOnlyList<string> eraseHandles, IReadOnlyList<NewAxis> create)
    {
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        DrawingUnit unit = DrawingStore.ReadUnit(Db, tr) ?? DrawingUnit.Metres;
        DrawingStore.EnsureAll(Db, tr);
        foreach (string handle in eraseHandles)
        {
            if (!TryOpen(tr, handle, out Entity? old)) continue;
            old!.UpgradeOpen();
            old.Erase();
        }

        BlockTableRecord modelSpace = ModelSpace.Open(Db, tr, OpenMode.ForWrite);
        foreach (NewAxis axis in create)
        {
            var line = new Line(ModelSpace.ToDrawing(unit, axis.Axis.A), ModelSpace.ToDrawing(unit, axis.Axis.B))
            {
                Layer = WifiHeatNames.WallLayer,
            };
            ModelSpace.Add(modelSpace, tr, line);
            WifiXData.WriteWall(line, "", axis.ThicknessM, generatedAxis: true);
        }

        tr.Commit();
    }

    protected override IReadOnlyList<EntityFaces> ReadEntityFaces(IReadOnlyCollection<string> handles)
    {
        var result = new List<EntityFaces>();
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        DrawingUnit unit = DrawingStore.ReadUnit(Db, tr) ?? DrawingUnit.Metres;
        foreach (string handle in handles)
        {
            if (!TryOpen(tr, handle, out Entity? entity) || entity is not (Line or Polyline)) continue;

            bool isWall = string.Equals(entity.Layer, WifiHeatNames.WallLayer, StringComparison.OrdinalIgnoreCase);
            bool closed = entity is Polyline polyline && ColumnReader.IsClosed(polyline, unit);
            result.Add(new EntityFaces(entity.Handle.ToString(), entity.Layer, isWall, FaceReader.StraightSegments(unit, (Curve)entity), closed));
        }

        tr.Commit();
        return result;
    }

    protected override IReadOnlyList<ClosedFigure> ReadClosedFigures(IReadOnlyCollection<string> handles)
    {
        var result = new List<ClosedFigure>();
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        DrawingUnit unit = DrawingStore.ReadUnit(Db, tr) ?? DrawingUnit.Metres;
        foreach (string handle in handles)
        {
            if (TryOpen(tr, handle, out Entity? entity)) result.AddRange(ColumnReader.Read(entity!, unit));
        }

        tr.Commit();
        return result;
    }

    protected override IReadOnlyList<ClosedFigure> ReadClosedFigures(BoundingBox windowM)
    {
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        IReadOnlyList<ClosedFigure> figures = ColumnReader.ReadInside(Db, tr, DrawingStore.ReadUnit(Db, tr) ?? DrawingUnit.Metres, windowM);
        tr.Commit();
        return figures;
    }

    protected override IReadOnlyList<string> WriteColumns(IReadOnlyList<NewColumn> create, IReadOnlyList<ColumnUpdate> update)
    {
        var handles = new List<string>(create.Count);
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        DrawingUnit unit = DrawingStore.ReadUnit(Db, tr) ?? DrawingUnit.Metres;
        DrawingStore.EnsureAll(Db, tr);
        foreach (ColumnUpdate column in update)
        {
            if (!TryOpen(tr, column.Handle, out Entity? entity)) continue;

            entity!.UpgradeOpen();
            WifiXData.WriteColumn(entity, column.MaterialId, column.Reinforcement);
        }

        BlockTableRecord modelSpace = ModelSpace.Open(Db, tr, OpenMode.ForWrite);
        foreach (NewColumn column in create)
        {
            var outline = new Polyline { Layer = WifiHeatNames.WallLayer };
            for (int i = 0; i < column.Footprint.Vertices.Count; i++)
            {
                Autodesk.AutoCAD.Geometry.Point3d p = ModelSpace.ToDrawing(unit, column.Footprint.Vertices[i]);
                outline.AddVertexAt(i, new Autodesk.AutoCAD.Geometry.Point2d(p.X, p.Y), 0, 0, 0);
            }

            outline.Closed = true;
            ModelSpace.Add(modelSpace, tr, outline);
            WifiXData.WriteColumn(outline, column.MaterialId, column.Reinforcement, generated: true);
            handles.Add(outline.Handle.ToString());
        }

        tr.Commit();
        return handles;
    }

    protected override IReadOnlyList<string> CreateAxes(IReadOnlyList<NewAxis> axes)
    {
        var handles = new List<string>(axes.Count);
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        DrawingUnit unit = DrawingStore.ReadUnit(Db, tr) ?? DrawingUnit.Metres;
        DrawingStore.EnsureAll(Db, tr);
        BlockTableRecord modelSpace = ModelSpace.Open(Db, tr, OpenMode.ForWrite);
        foreach (NewAxis axis in axes)
        {
            var line = new Line(ModelSpace.ToDrawing(unit, axis.Axis.A), ModelSpace.ToDrawing(unit, axis.Axis.B))
            {
                Layer = WifiHeatNames.WallLayer,
            };
            ModelSpace.Add(modelSpace, tr, line);
            WifiXData.WriteWall(line, axis.MaterialId ?? "", axis.ThicknessM, generatedAxis: false);
            handles.Add(line.Handle.ToString());
        }

        tr.Commit();
        return handles;
    }

    protected override void RetireFaces(IReadOnlyList<string> handles)
    {
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        DrawingStore.EnsureRegApp(Db, tr);
        DrawingStore.EnsureLayer(Db, tr, WifiHeatNames.FaceLayer, 8);
        foreach (string handle in handles)
        {
            if (!TryOpen(tr, handle, out Entity? entity)) continue;

            entity!.UpgradeOpen();
            WifiXData.Clear(entity);
            entity.Layer = WifiHeatNames.FaceLayer;
        }

        tr.Commit();
    }

    protected override IReadOnlyList<string> WriteWalls(IReadOnlyList<WallAssignment> assignments)
    {
        var missing = new List<string>();
        using DocumentLock _ = Document.LockDocument();
        using Transaction tr = Db.TransactionManager.StartTransaction();
        DrawingStore.EnsureAll(Db, tr);
        foreach (WallAssignment a in assignments)
        {
            if (!TryOpen(tr, a.Handle, out Entity? entity) || entity is not (Line or Polyline))
            {
                missing.Add(a.Handle);
                continue;
            }

            entity.UpgradeOpen();
            WifiXData.WriteWall(entity, a.MaterialId, a.ThicknessM);
            entity.Layer = WifiHeatNames.WallLayer;
        }

        tr.Commit();
        return missing;
    }

    protected override string? SaveAccessPoint(AccessPoint ap, RadioSettings settings)
    {
        using DocumentLock _ = Document.LockDocument();
        DrawingUnit? unit = DrawingStore.ReadUnit(Db);
        if (unit is null) return "El dibujo no tiene unidad guardada; el AP no se dibujó. Ejecutar WIFISETUP.";

        using Transaction tr = Db.TransactionManager.StartTransaction();
        AccessPointMarker.Draw(Db, tr, unit.Value, ap, settings);
        tr.Commit();
        return null;
    }

    protected override OverlayOutcome DrawResult(RssiGrid grid, HeatmapImage image, Legend legend, int fadePercent, OverlayImageKind kind)
    {
        using DocumentLock _ = Document.LockDocument();
        DrawingUnit unit = DrawingStore.ReadUnit(Db) ?? DrawingUnit.Metres;
        (string folder, string baseName, bool saved) = OverlayImageLocation();
        OverlayResult overlay;
        using (Transaction tr = Db.TransactionManager.StartTransaction())
        {
            DrawingStore.EnsureAll(Db, tr);
            overlay = RasterOverlayRenderer.Render(Db, tr, unit, image, folder, baseName, fadePercent, kind);
            LegendRenderer.Render(Db, tr, unit, grid.Spec, legend, fadePercent);
            tr.Commit();
        }

        var warnings = new List<string>(overlay.Warnings);
        if (!saved) warnings.Add("El dibujo no está guardado: la imagen del mapa se guardó en la carpeta temporal. Guardar el dibujo y volver a calcular para dejarla junto al DWG.");
        return new OverlayOutcome(overlay.ImagePath, warnings);
    }

    protected override string? ApplyOverlayFade(int percent, RssiGrid? grid, Legend legend)
    {
        using DocumentLock _ = Document.LockDocument();
        try
        {
            using Transaction tr = Db.TransactionManager.StartTransaction();
            RasterOverlayRenderer.SetFade(Db, tr, percent);
            if (grid is not null) LegendRenderer.Render(Db, tr, DrawingStore.ReadUnit(Db, tr) ?? DrawingUnit.Metres, grid.Spec, legend, percent);
            tr.Commit();
            return null;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            return $"No se pudo aplicar la atenuación al mapa dibujado ({ex.Message}); se usará en el próximo cálculo.";
        }
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

    private bool TryOpen(Transaction tr, string handleText, out Entity? entity)
    {
        entity = null;
        if (!long.TryParse(handleText, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value)) return false;
        if (!Db.TryGetObjectId(new Handle(value), out ObjectId id) || id.IsErased) return false;

        entity = tr.GetObject(id, OpenMode.ForRead) as Entity;
        return entity is not null;
    }
}
