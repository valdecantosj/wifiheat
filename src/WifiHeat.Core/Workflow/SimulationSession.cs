using System.Globalization;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;

namespace WifiHeat.Core.Workflow;

/// <summary>
/// Flujo de simulación de un documento: validar → AP → calcular → consultar.
/// Lo comparten comandos y panel; no depende de AutoCAD.
/// </summary>
public sealed class SimulationSession(MaterialCatalog catalog)
{
    public const double DefaultCellSizeM = 0.5;
    public const double MinCellSizeM = 0.10;
    public const double MaxCellSizeM = 5;

    private const double MinAreaSideM = 1e-6;

    public MaterialCatalog Catalog { get; } = catalog;

    public RadioSettings Settings { get; private set; } = new();

    public double CellSizeM { get; private set; } = DefaultCellSizeM;

    /// <summary>Atenuación del mapa en el dibujo y en la exportación, en %. No cambia el cálculo.</summary>
    public int OverlayFadePercent { get; private set; } = OverlayFade.DefaultPercent;

    public OperationResult<int> SetOverlayFade(int percent)
    {
        if (OverlayFade.Error(percent) is { } error) return OperationResult<int>.Fail(error);
        OverlayFadePercent = percent;
        return OperationResult<int>.Ok(percent);
    }

    public IReadOnlyList<Wall> Walls { get; private set; } = [];

    public ValidationReport? Validation { get; private set; }

    public AccessPoint? AccessPoint { get; private set; }

    public RssiGrid? LastGrid
    {
        get => _lastGrid;
        private set
        {
            _lastGrid = value;
            _heatmap = null;
        }
    }

    /// <summary>Imagen continua del último cálculo; la comparten el mapa del dibujo y la exportación.</summary>
    public HeatmapImage? Heatmap() => _lastGrid is null ? null : _heatmap ??= HeatmapRasterizer.Render(_lastGrid);

    /// <summary>Área de cobertura definida por el usuario. Si es null se usa el rectángulo de los muros.</summary>
    public Polygon2? CoverageArea { get; private set; }

    private SimulationScene? _scene;
    private RssiGrid? _lastGrid;
    private HeatmapImage? _heatmap;

    /// <summary>Carga los muros leídos del plano y los valida. Invalida el último cálculo.</summary>
    public ValidationReport LoadWalls(IReadOnlyList<Wall> walls, IEnumerable<ValidationIssue>? hostIssues = null)
    {
        Walls = walls;
        Validation = SceneValidator.Validate(walls, Catalog, hostIssues);
        _scene = Validation.IsValid ? new SimulationScene(walls, Catalog) : null;
        LastGrid = null;
        return Validation;
    }

    public void PlaceAccessPoint(Vec2 position)
    {
        AccessPoint = new AccessPoint(position);
        LastGrid = null;
    }

    public void SetCoverageArea(Polygon2? area)
    {
        CoverageArea = area;
        LastGrid = null;
    }

    public OperationResult<RadioSettings> Configure(RadioSettings settings, double cellSizeM)
    {
        var errors = settings.Validate().ToList();
        if (ResolutionError(cellSizeM) is { } resolution) errors.Add(resolution);
        if (errors.Count > 0) return new(null, errors, []);

        Settings = settings;
        CellSizeM = cellSizeM;
        LastGrid = null;
        return OperationResult<RadioSettings>.Ok(settings);
    }

    /// <summary>Tamaño de celda de la grilla, en metros. El tope por cantidad de celdas se controla al calcular.</summary>
    public OperationResult<double> SetResolution(double cellSizeM)
    {
        if (ResolutionError(cellSizeM) is { } error) return OperationResult<double>.Fail(error);

        CellSizeM = cellSizeM;
        LastGrid = null;
        return OperationResult<double>.Ok(cellSizeM);
    }

    public static string? ResolutionError(double cellSizeM) =>
        double.IsFinite(cellSizeM) && cellSizeM >= MinCellSizeM && cellSizeM <= MaxCellSizeM
            ? null
            : string.Create(CultureInfo.GetCultureInfo("es-AR"), $"La resolución debe estar entre {MinCellSizeM:0.00} y {MaxCellSizeM:0} m.");

    public OperationResult<RssiGrid> Calculate()
    {
        if (Validation is null) return OperationResult<RssiGrid>.Fail("Primero validar el plano (WIFIVALIDATE).");
        if (_scene is null) return OperationResult<RssiGrid>.Fail($"El plano tiene {Validation.ErrorCount} error(es) de validación. Corregirlos y validar de nuevo.");
        if (AccessPoint is null) return OperationResult<RssiGrid>.Fail("Falta ubicar el AP (WIFIADDAP).");
        if (AccessPointInsideColumn() is { } inside) return OperationResult<RssiGrid>.Fail(inside);

        Polygon2 area;
        if (CoverageArea is not null)
        {
            area = CoverageArea;
        }
        else
        {
            BoundingBox walls = _scene.Bounds();
            if (walls.Width < MinAreaSideM || walls.Height < MinAreaSideM)
            {
                return OperationResult<RssiGrid>.Fail("Los muros no encierran superficie (todos alineados). Definir el área de cobertura con WIFIAREA.");
            }

            area = Polygon2.Rectangle(walls);
        }

        BoundingBox bounds = area.Bounds;
        if (bounds.Width / CellSizeM * (bounds.Height / CellSizeM) > GridSpec.MaxGridCells)
        {
            return OperationResult<RssiGrid>.Fail(FormattableString.Invariant(
                $"{ValidationIssue.LargeArea}: el área a calcular mide {bounds.Width:0.#} × {bounds.Height:0.#} m y supera el máximo de {GridSpec.MaxGridCells} celdas. Revisar la unidad del dibujo, el área de cobertura o los muros alejados."));
        }

        GridSpec spec = GridSpec.Cover(bounds, CellSizeM);

        var warnings = new List<string>();
        if (spec.CellCount > GridSpec.LargeGridWarningCells)
        {
            warnings.Add($"{ValidationIssue.LargeArea}: {spec.CellCount:N0} celdas ({bounds.Width:0.#} × {bounds.Height:0.#} m). " +
                         "Revisar unidades del plano o usar una resolución mayor.");
        }

        LastGrid = GridCalculator.Calculate(_scene, AccessPoint, Settings, spec, area);
        return OperationResult<RssiGrid>.Ok(LastGrid, warnings);
    }

    /// <summary>Consulta en el punto exacto indicado (no el centro de celda).</summary>
    public OperationResult<PointCalculation> Query(Vec2 point)
    {
        if (_scene is null) return OperationResult<PointCalculation>.Fail("El plano no está validado sin errores.");
        if (AccessPoint is null) return OperationResult<PointCalculation>.Fail("Falta ubicar el AP (WIFIADDAP).");
        if (AccessPointInsideColumn() is { } inside) return OperationResult<PointCalculation>.Fail(inside);
        if (_scene.ColumnContaining(point) is { } column)
        {
            return OperationResult<PointCalculation>.Fail(
                $"El punto consultado está dentro de la columna {column.Wall.Id}: ahí no hay señal que estimar. Elegir un punto fuera de la columna.");
        }

        return OperationResult<PointCalculation>.Ok(PointCalculator.Calculate(_scene, AccessPoint, Settings, point));
    }

    /// <summary>Mensaje si el AP está dentro de una columna (o sobre su borde); null si no.</summary>
    private string? AccessPointInsideColumn() =>
        AccessPoint is not null && _scene?.ColumnContaining(AccessPoint.Position) is { } column
            ? $"El AP está dentro de la columna {column.Wall.Id}: no se calcula. Ubicarlo fuera de la columna."
            : null;
}
