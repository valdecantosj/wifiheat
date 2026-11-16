using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Walls;
using WifiHeat.Core.Workflow;

namespace WifiHeat.Core.Service;

/// <summary>Muro de simulación tal como está en el dibujo. Longitud en metros.</summary>
public sealed record WallInfo(string Handle, double LengthM, string Layer, string? MaterialId, double? ThicknessM);

public sealed record AccessPointInfo(string Name, Vec2 PositionM, Band Band, double FrequencyMHz, double TxPowerDbm);

/// <summary>Un problema de validación con el handle de la entidad a corregir (null si es general).</summary>
public sealed record ValidationItem(IssueSeverity Severity, string Code, string? Handle, string Message)
{
    public override string ToString() => new ValidationIssue(Severity, Code, Handle, Message).ToString();
}

public sealed record ValidationSummary(int WallCount, int ErrorCount, int WarningCount, BoundingBox? WallBounds, IReadOnlyList<ValidationItem> Items)
{
    public bool IsValid => ErrorCount == 0;
}

/// <summary>Parámetros que el usuario puede cambiar: enlace, resolución de la grilla y atenuación del mapa.</summary>
public sealed record SimulationSettings(
    Band Band,
    double TxPowerDbm,
    double TxAntennaGainDbi,
    double RxAntennaGainDbi,
    double TxCableLossDb,
    double RxCableLossDb,
    double CellSizeM,
    int OverlayFadePercent);

public sealed record CalculationSummary(
    int Cells,
    int Columns,
    int Rows,
    double CellSizeM,
    int CellsWithValue,
    double MinDbm,
    double MaxDbm,
    double CalculationMs,
    double ImageMs,
    int ImageWidthPx,
    int ImageHeightPx,
    double PixelSizeM,
    int OverlayFadePercent,
    string? ImagePath,
    string AreaDescription,
    IReadOnlyList<string> Warnings);

public sealed record QueryResult(Vec2 PointM, double ReceivedPowerDbm, SignalLevel Level, PointCalculation Calculation, string Explanation);

/// <summary>Lo que el servicio necesita leer del dibujo.</summary>
public sealed record DrawingWall(Wall Wall, string Layer);

public sealed record DrawingContents(IReadOnlyList<DrawingWall> Walls, IReadOnlyList<ValidationIssue> Issues);

public sealed record CoverageAreaRead(Polygon2? Polygon, string Description, string? Warning, string? Handle = null);

public sealed record WallAssignment(string Handle, string MaterialId, double ThicknessM);

public sealed record OverlayOutcome(string? ImagePath, IReadOnlyList<string> Warnings);

/// <summary>Unidad guardada en el dibujo (null si falta elegirla) y la que sugieren sus variables.</summary>
public sealed record DrawingSetup(DrawingUnit? Unit, DrawingUnit SuggestedUnit, string DeclaredUnits);

/// <summary>Área de cobertura vigente. IsCustom false = rectángulo de los muros.</summary>
public sealed record CoverageAreaInfo(bool IsCustom, string Description, string? Handle, int VertexCount, double AreaM2, BoundingBox? Bounds, string? Warning);

/// <summary>
/// Capa con segmentos dentro de la zona de la planta y cuántos ejes daría por sí sola.
/// ExplicitOnly: no se procesa salvo que el usuario la nombre (capa 0, ventanas).
/// </summary>
public sealed record AxisLayerInfo(string Layer, int SegmentCount, int AxisCount, bool IsSuggested, bool ExplicitOnly);

public sealed record AxisSurvey(BoundingBox WindowM, IReadOnlyList<AxisLayerInfo> Layers)
{
    public string? SuggestedLayer => Layers.FirstOrDefault(l => l.IsSuggested)?.Layer;
}

/// <summary>Resultado de generar ejes. Kept: ejes anteriores que coinciden y se conservan; Removed: anteriores borrados.</summary>
public sealed record AxisGeneration(
    int SegmentCount,
    int AxisCount,
    int Created,
    int Kept,
    int Removed,
    double MinThicknessM,
    double MaxThicknessM,
    double MergeGapM,
    IReadOnlyList<string> Unpaired,
    IReadOnlyList<string> PartlyUnpaired);

public sealed record LayerFaces(string Layer, IReadOnlyList<FaceSegment> Faces);

/// <summary>Eje creado antes por la generación automática, identificado por su marca en el dibujo.</summary>
public sealed record GeneratedAxis(string Handle, Segment2 Axis);

public sealed record NewAxis(Segment2 Axis, double ThicknessM);
