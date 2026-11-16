using System.Diagnostics;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Walls;
using WifiHeat.Core.Workflow;

namespace WifiHeat.Core.Service;

/// <summary>
/// Implementación común de <see cref="IWifiHeatService"/> sobre una <see cref="SimulationSession"/>.
/// El anfitrión (AutoCAD, o un dibujo falso en las pruebas) solo implementa la lectura y escritura del dibujo.
/// </summary>
public abstract class WifiHeatService(SimulationSession session) : IWifiHeatService
{
    public const double DefaultThicknessM = 0.15;

    private const double SameAxisToleranceM = 0.01;
    private const string MissingUnit = "El dibujo no tiene unidad guardada. Ejecutar WIFISETUP.";
    private const string NoFacesInWindow = "No hay líneas ni polilíneas enteras dentro de la zona de la planta.";

    private DrawingContents _drawingContents = new([], []);

    public SimulationSession Session { get; } = session;

    public SimulationSettings Settings
    {
        get
        {
            RadioSettings r = Session.Settings;
            return new SimulationSettings(r.Band, r.TxPowerDbm, r.TxAntennaGainDbi, r.RxAntennaGainDbi, r.TxCableLossDb, r.RxCableLossDb,
                Session.CellSizeM, Session.OverlayFadePercent);
        }
    }

    public AccessPointInfo? AccessPoint => Session.AccessPoint is { } ap
        ? new AccessPointInfo(ap.Name, ap.Position, Session.Settings.Band, Session.Settings.FrequencyMHz, Session.Settings.TxPowerDbm)
        : null;

    public IReadOnlyList<Material> Materials => [.. Session.Catalog.All];

    public bool HasResult => Session.LastGrid is not null;

    public abstract string SuggestedExportPath { get; }

    public CalculationSummary? LastCalculation { get; private set; }

    public DrawingSetup GetDrawingSetup() => ReadSetup();

    public OperationResult<DrawingUnit> SetDrawingUnit(DrawingUnit unit)
    {
        if (!Enum.IsDefined(unit)) return OperationResult<DrawingUnit>.Fail("Unidad desconocida.");

        WriteUnit(unit);
        LoadWalls();
        return OperationResult<DrawingUnit>.Ok(unit);
    }

    /// <summary>Si el dibujo no cambió desde la lectura anterior, el último cálculo sigue vigente.</summary>
    public IReadOnlyList<WallInfo> LoadWalls()
    {
        DrawingContents contents = ReadDrawing();
        if (Session.Validation is null || !SameContents(_drawingContents, contents))
        {
            Session.LoadWalls([.. contents.Walls.Select(w => w.Wall)], contents.Issues);
        }

        _drawingContents = contents;
        return [.. contents.Walls.Select(w => new WallInfo(w.Wall.Id, w.Wall.Segments.Sum(s => s.Length), w.Layer, w.Wall.MaterialId, w.Wall.ThicknessM))];
    }

    public ValidationSummary Validate()
    {
        LoadWalls();
        return Summary();
    }

    public OperationResult<int> AssignMaterial(IReadOnlyCollection<string> handles, string materialId, double? thicknessM)
    {
        if (handles.Count == 0) return OperationResult<int>.Fail("No se eligió ningún muro.");
        if (string.IsNullOrWhiteSpace(materialId) || !Session.Catalog.TryGet(materialId.Trim(), out Material material))
        {
            string ids = string.Join("/", Session.Catalog.All.Select(m => m.Id));
            return OperationResult<int>.Fail($"{ValidationIssue.UnknownMaterial}: '{materialId}' no está en el catálogo. Valores posibles: {ids}. No se modificó ningún muro.");
        }

        if (thicknessM is { } t && (!double.IsFinite(t) || t <= 0))
        {
            return OperationResult<int>.Fail("El espesor debe ser un número mayor que 0 m. No se modificó ningún muro.");
        }

        LoadWalls();
        var existing = _drawingContents.Walls.ToDictionary(w => w.Wall.Id, w => w.Wall.ThicknessM, StringComparer.OrdinalIgnoreCase);
        List<WallAssignment> assignments = [.. handles.Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(h => new WallAssignment(h, material.Id, thicknessM ?? existing.GetValueOrDefault(h) ?? DefaultThicknessM))];

        IReadOnlyList<string> notFound = WriteWalls(assignments);
        LoadWalls();
        int updated = assignments.Count - notFound.Count;
        IReadOnlyList<string> warnings = notFound.Count == 0 ? [] : [$"No se encontraron en el dibujo: {string.Join(", ", notFound)}."];
        return updated == 0
            ? new OperationResult<int>(0, ["Ninguna de las entidades elegidas existe en el dibujo."], warnings)
            : OperationResult<int>.Ok(updated, warnings);
    }

    public OperationResult<AxisSurvey> SurveyAxisLayers(BoundingBox windowM, double mergeGapM = AxisDefaults.MergeGapM)
    {
        if (AxisPrecondition(mergeGapM) is { } error) return OperationResult<AxisSurvey>.Fail(error);

        IReadOnlyList<LayerFaces> faces = ReadFaces(windowM);
        if (faces.Count == 0) return OperationResult<AxisSurvey>.Fail(NoFacesInWindow);

        var options = new AxisDetectionOptions { MaxMergeGapM = mergeGapM };
        var counted = faces
            .Select(l => (l.Layer, Segments: l.Faces.Count, Axes: WallAxisDetector.Detect(l.Faces, options).Axes.Count, ExplicitOnly: AxisDefaults.IsExplicitOnly(l.Layer)))
            .ToList();

        string? suggested = counted.FirstOrDefault(c => string.Equals(c.Layer, AxisDefaults.PreferredLayer, StringComparison.OrdinalIgnoreCase)).Layer
            ?? counted.Where(c => !c.ExplicitOnly && c.Axes > 0)
                .OrderByDescending(c => c.Layer.Contains("muro", StringComparison.OrdinalIgnoreCase) || c.Layer.Contains("wall", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(c => c.Axes)
                .FirstOrDefault().Layer;

        List<AxisLayerInfo> layers = [.. counted
            .OrderByDescending(c => c.Layer == suggested)
            .ThenBy(c => c.ExplicitOnly)
            .ThenByDescending(c => c.Axes)
            .ThenByDescending(c => c.Segments)
            .ThenBy(c => c.Layer, StringComparer.OrdinalIgnoreCase)
            .Select(c => new AxisLayerInfo(c.Layer, c.Segments, c.Axes, c.Layer == suggested, c.ExplicitOnly))];
        return OperationResult<AxisSurvey>.Ok(new AxisSurvey(windowM, layers));
    }

    public OperationResult<AxisGeneration> GenerateAxes(BoundingBox windowM, IReadOnlyCollection<string> layers, double mergeGapM = AxisDefaults.MergeGapM)
    {
        if (AxisPrecondition(mergeGapM) is { } error) return OperationResult<AxisGeneration>.Fail(error);

        string[] wanted = [.. layers.Select(l => l.Trim()).Where(l => l.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (wanted.Length == 0) return OperationResult<AxisGeneration>.Fail("No se eligió ninguna capa. No se creó ningún eje.");

        IReadOnlyList<LayerFaces> faces = ReadFaces(windowM);
        if (faces.Count == 0) return OperationResult<AxisGeneration>.Fail(NoFacesInWindow);

        var byLayer = faces.ToDictionary(l => l.Layer, l => l.Faces, StringComparer.OrdinalIgnoreCase);
        string[] missing = [.. wanted.Where(l => !byLayer.ContainsKey(l))];
        if (missing.Length > 0)
        {
            return OperationResult<AxisGeneration>.Fail($"Sin segmentos dentro de la zona en la capa '{string.Join("', '", missing)}'. No se creó ningún eje.");
        }

        List<FaceSegment> chosen = [.. wanted.SelectMany(l => byLayer[l])];
        AxisDetection detection = WallAxisDetector.Detect(chosen, new AxisDetectionOptions { MaxMergeGapM = mergeGapM });
        if (detection.Axes.Count == 0)
        {
            return OperationResult<AxisGeneration>.Ok(
                new AxisGeneration(chosen.Count, 0, 0, 0, 0, 0, 0, mergeGapM, detection.Unpaired, detection.PartlyUnpaired),
                ["No se encontró ningún par de caras paralelas; el dibujo no se modificó."]);
        }

        List<GeneratedAxis> previous = [.. ReadGeneratedAxes(windowM.Expand(SameAxisToleranceM))];
        var create = new List<NewAxis>();
        int kept = 0;
        foreach (WallAxis axis in detection.Axes)
        {
            int match = previous.FindIndex(p => SameAxis(p.Axis, axis.Axis));
            if (match >= 0)
            {
                previous.RemoveAt(match);
                kept++;
            }
            else
            {
                create.Add(new NewAxis(axis.Axis, Math.Round(axis.ThicknessM, 3)));
            }
        }

        ReplaceAxes([.. previous.Select(p => p.Handle)], create);
        LoadWalls();
        return OperationResult<AxisGeneration>.Ok(new AxisGeneration(
            chosen.Count, detection.Axes.Count, create.Count, kept, previous.Count,
            detection.Axes.Min(a => a.ThicknessM), detection.Axes.Max(a => a.ThicknessM), mergeGapM,
            detection.Unpaired, detection.PartlyUnpaired));
    }

    public OperationResult<AccessPointInfo> SetAccessPoint(Vec2 positionM)
    {
        if (!double.IsFinite(positionM.X) || !double.IsFinite(positionM.Y)) return OperationResult<AccessPointInfo>.Fail("La ubicación del AP no es válida.");

        Session.PlaceAccessPoint(positionM);
        if (SaveAccessPoint(Session.AccessPoint!, Session.Settings) is { } error) return OperationResult<AccessPointInfo>.Fail(error);
        return OperationResult<AccessPointInfo>.Ok(AccessPoint!);
    }

    public OperationResult<SimulationSettings> Configure(SimulationSettings settings)
    {
        if (!Enum.IsDefined(settings.Band)) return OperationResult<SimulationSettings>.Fail("Banda desconocida.");

        RadioSettings radio = Session.Settings with
        {
            Band = settings.Band,
            TxPowerDbm = settings.TxPowerDbm,
            TxAntennaGainDbi = settings.TxAntennaGainDbi,
            RxAntennaGainDbi = settings.RxAntennaGainDbi,
            TxCableLossDb = settings.TxCableLossDb,
            RxCableLossDb = settings.RxCableLossDb,
        };

        var errors = new List<string>();
        if (new[] { settings.TxPowerDbm, settings.TxAntennaGainDbi, settings.RxAntennaGainDbi, settings.TxCableLossDb, settings.RxCableLossDb }.Any(v => !double.IsFinite(v)))
        {
            errors.Add("Hay un valor que no es un número.");
        }

        errors.AddRange(radio.Validate());
        if (SimulationSession.ResolutionError(settings.CellSizeM) is { } resolution) errors.Add(resolution);
        if (OverlayFade.Error(settings.OverlayFadePercent) is { } fade) errors.Add(fade);
        if (errors.Count > 0) return new OperationResult<SimulationSettings>(null, errors, []);

        bool radioChanged = radio != Session.Settings;
        Session.Configure(radio, settings.CellSizeM);
        Session.SetOverlayFade(settings.OverlayFadePercent);

        var warnings = new List<string>();
        if (radioChanged && Session.AccessPoint is { } ap && SaveAccessPoint(ap, Session.Settings) is { } error) warnings.Add(error);
        return OperationResult<SimulationSettings>.Ok(Settings, warnings);
    }

    public CoverageAreaInfo GetCoverageArea() => AreaInfo(ReadCoverageArea());

    public OperationResult<CoverageAreaInfo> SetCoverageArea(string polylineHandle)
    {
        if (string.IsNullOrWhiteSpace(polylineHandle)) return OperationResult<CoverageAreaInfo>.Fail("No se eligió ninguna polilínea.");
        return ChangeCoverageArea(polylineHandle.Trim());
    }

    public OperationResult<CoverageAreaInfo> ClearCoverageArea() => ChangeCoverageArea(null);

    public OperationResult<int> SetOverlayFade(int percent)
    {
        OperationResult<int> set = Session.SetOverlayFade(percent);
        if (!set.Succeeded) return set;

        string? warning = ApplyOverlayFade(percent, Session.LastGrid, Legend.For(Session.Settings));
        return OperationResult<int>.Ok(percent, warning is null ? [] : [warning]);
    }

    public OperationResult<CalculationSummary> Calculate()
    {
        ValidationSummary validation = Validate();
        if (!validation.IsValid)
        {
            return new OperationResult<CalculationSummary>(null,
                [.. validation.Items.Where(i => i.Severity == IssueSeverity.Error).Select(i => i.ToString()),
                 $"No se calcula: el plano tiene {validation.ErrorCount} error(es) de validación."], []);
        }

        CoverageAreaRead area = ReadCoverageArea();
        Session.SetCoverageArea(area.Polygon);
        var warnings = new List<string>();
        if (area.Warning is not null) warnings.Add(area.Warning);

        var watch = Stopwatch.StartNew();
        OperationResult<RssiGrid> result = Session.Calculate();
        watch.Stop();
        if (!result.Succeeded) return new OperationResult<CalculationSummary>(null, result.Errors, warnings);
        warnings.AddRange(result.Warnings);

        RssiGrid grid = result.Value!;
        var imageWatch = Stopwatch.StartNew();
        HeatmapImage image = Session.Heatmap()!;
        OverlayOutcome overlay = DrawResult(grid, image, Legend.For(Session.Settings), Session.OverlayFadePercent);
        imageWatch.Stop();
        warnings.AddRange(overlay.Warnings);

        GridSpec spec = grid.Spec;
        LastCalculation = new CalculationSummary(
            spec.CellCount, spec.Columns, spec.Rows, spec.CellSizeM, grid.ValueCount, grid.Min, grid.Max,
            watch.Elapsed.TotalMilliseconds, imageWatch.Elapsed.TotalMilliseconds,
            image.Width, image.Height, image.PixelSizeM, Session.OverlayFadePercent, overlay.ImagePath, area.Description, warnings);
        return OperationResult<CalculationSummary>.Ok(LastCalculation, warnings);
    }

    public OperationResult<QueryResult> Query(Vec2 pointM)
    {
        if (!double.IsFinite(pointM.X) || !double.IsFinite(pointM.Y)) return OperationResult<QueryResult>.Fail("El punto no es válido.");
        if (Session.Validation is null) LoadWalls();

        OperationResult<PointCalculation> result = Session.Query(pointM);
        if (!result.Succeeded) return new OperationResult<QueryResult>(null, result.Errors, []);

        PointCalculation c = result.Value!;
        return OperationResult<QueryResult>.Ok(new QueryResult(pointM, c.ReceivedPowerDbm, SignalScale.Classify(c.ReceivedPowerDbm), c, Explanation.Format(c)));
    }

    public OperationResult<string> ExportPng(string path)
    {
        if (Session.LastGrid is null || Session.AccessPoint is null) return OperationResult<string>.Fail("No hay un cálculo vigente. Ejecutar WIFICALCULATE.");
        if (string.IsNullOrWhiteSpace(path)) return OperationResult<string>.Fail("Falta la ruta del archivo.");

        string full;
        try
        {
            full = Path.GetFullPath(Path.HasExtension(path) ? path : path + ".png");
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return OperationResult<string>.Fail($"La ruta '{path}' no es válida.");
        }

        if (!string.Equals(Path.GetExtension(full), ".png", StringComparison.OrdinalIgnoreCase))
        {
            return OperationResult<string>.Fail("El archivo debe tener extensión .png.");
        }

        try
        {
            WritePng(full, Session.Walls, Session.Heatmap()!, Session.AccessPoint, Legend.For(Session.Settings), Session.OverlayFadePercent);
            return OperationResult<string>.Ok(full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            return OperationResult<string>.Fail($"No se pudo guardar {full}: {ex.Message}");
        }
    }

    private ValidationSummary Summary()
    {
        ValidationReport report = Session.Validation!;
        var points = Session.Walls.SelectMany(w => w.Segments).SelectMany(s => new[] { s.A, s.B }).ToList();
        BoundingBox? bounds = points.Count > 0 ? BoundingBox.FromPoints(points) : null;
        return new ValidationSummary(Session.Walls.Count, report.ErrorCount, report.WarningCount, bounds,
            [.. report.Issues.Select(i => new ValidationItem(i.Severity, i.Code, i.EntityRef, i.Message))]);
    }

    private OperationResult<CoverageAreaInfo> ChangeCoverageArea(string? handle)
    {
        if (ReadSetup().Unit is null) return OperationResult<CoverageAreaInfo>.Fail(MissingUnit);

        OperationResult<CoverageAreaRead> written = WriteCoverageArea(handle);
        if (!written.Succeeded) return new OperationResult<CoverageAreaInfo>(null, written.Errors, written.Warnings);

        Polygon2? polygon = written.Value!.Polygon;
        if (polygon is not null || Session.CoverageArea is not null) Session.SetCoverageArea(polygon);
        return OperationResult<CoverageAreaInfo>.Ok(AreaInfo(written.Value), written.Warnings);
    }

    private static CoverageAreaInfo AreaInfo(CoverageAreaRead read) => new(
        read.Polygon is not null, read.Description, read.Handle,
        read.Polygon?.Vertices.Count ?? 0, read.Polygon?.AreaM2 ?? 0, read.Polygon?.Bounds, read.Warning);

    private string? AxisPrecondition(double mergeGapM) =>
        ReadSetup().Unit is null ? MissingUnit : AxisDetectionOptions.MergeGapError(mergeGapM);

    private static bool SameAxis(Segment2 a, Segment2 b) =>
        (Vec2.Distance(a.A, b.A) <= SameAxisToleranceM && Vec2.Distance(a.B, b.B) <= SameAxisToleranceM) ||
        (Vec2.Distance(a.A, b.B) <= SameAxisToleranceM && Vec2.Distance(a.B, b.A) <= SameAxisToleranceM);

    private static bool SameContents(DrawingContents a, DrawingContents b) =>
        a.Issues.SequenceEqual(b.Issues) &&
        a.Walls.Count == b.Walls.Count &&
        a.Walls.Zip(b.Walls).All(p =>
            p.First.Layer == p.Second.Layer &&
            p.First.Wall.Id == p.Second.Wall.Id &&
            p.First.Wall.MaterialId == p.Second.Wall.MaterialId &&
            p.First.Wall.ThicknessM == p.Second.Wall.ThicknessM &&
            p.First.Wall.Segments.SequenceEqual(p.Second.Wall.Segments));

    protected abstract DrawingSetup ReadSetup();

    /// <summary>Guarda la unidad y crea capas y aplicación registrada si faltan.</summary>
    protected abstract void WriteUnit(DrawingUnit unit);

    /// <summary>Líneas y tramos rectos de polilíneas enteros dentro de la zona, por capa, fuera de las capas propias.</summary>
    protected abstract IReadOnlyList<LayerFaces> ReadFaces(BoundingBox windowM);

    /// <summary>Ejes generados antes (con marca) que están enteros dentro de la zona.</summary>
    protected abstract IReadOnlyList<GeneratedAxis> ReadGeneratedAxes(BoundingBox windowM);

    /// <summary>Borra los ejes indicados y crea los nuevos en la capa de muros, marcados como generados y sin material.</summary>
    protected abstract void ReplaceAxes(IReadOnlyList<string> eraseHandles, IReadOnlyList<NewAxis> create);

    /// <summary>Guarda la polilínea como área de cobertura (null la quita) y devuelve lo guardado, o el motivo del rechazo.</summary>
    protected abstract OperationResult<CoverageAreaRead> WriteCoverageArea(string? polylineHandle);

    /// <summary>Aplica la atenuación al mapa dibujado y, si hay cálculo vigente, rehace la leyenda. Devuelve un aviso si no se pudo.</summary>
    protected abstract string? ApplyOverlayFade(int percent, RssiGrid? grid, Legend legend);

    /// <summary>Muros de la capa de simulación (en metros) y problemas de lectura, como falta de unidad.</summary>
    protected abstract DrawingContents ReadDrawing();

    /// <summary>Área de cobertura guardada; Polygon null = rectángulo de los muros.</summary>
    protected abstract CoverageAreaRead ReadCoverageArea();

    /// <summary>Escribe material y espesor y pasa las entidades a la capa de muros. Devuelve los handles que no existen.</summary>
    protected abstract IReadOnlyList<string> WriteWalls(IReadOnlyList<WallAssignment> assignments);

    /// <summary>Dibuja el marcador del AP con su banda y potencia. Devuelve un mensaje si no se pudo.</summary>
    protected abstract string? SaveAccessPoint(AccessPoint ap, RadioSettings settings);

    /// <summary>Reemplaza mapa y leyenda del dibujo.</summary>
    protected abstract OverlayOutcome DrawResult(RssiGrid grid, HeatmapImage image, Legend legend, int fadePercent);

    protected abstract void WritePng(string path, IReadOnlyList<Wall> walls, HeatmapImage image, AccessPoint ap, Legend legend, int fadePercent);
}
