using System.Diagnostics;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;

namespace WifiHeat.Core.Service;

/// <summary>
/// Implementación común de <see cref="IWifiHeatService"/> sobre una <see cref="SimulationSession"/>.
/// El anfitrión (AutoCAD, o un dibujo falso en las pruebas) solo implementa la lectura y escritura del dibujo.
/// </summary>
public abstract class WifiHeatService(SimulationSession session) : IWifiHeatService
{
    public const double DefaultThicknessM = 0.15;

    private IReadOnlyList<DrawingWall> _drawingWalls = [];

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

    public IReadOnlyList<WallInfo> LoadWalls()
    {
        DrawingContents contents = ReadDrawing();
        _drawingWalls = contents.Walls;
        Session.LoadWalls([.. contents.Walls.Select(w => w.Wall)], contents.Issues);
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
        var existing = _drawingWalls.ToDictionary(w => w.Wall.Id, w => w.Wall.ThicknessM, StringComparer.OrdinalIgnoreCase);
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
        return OperationResult<CalculationSummary>.Ok(new CalculationSummary(
            spec.CellCount, spec.Columns, spec.Rows, spec.CellSizeM, grid.ValueCount, grid.Min, grid.Max,
            watch.Elapsed.TotalMilliseconds, imageWatch.Elapsed.TotalMilliseconds,
            image.Width, image.Height, image.PixelSizeM, Session.OverlayFadePercent, overlay.ImagePath, area.Description, warnings), warnings);
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
