using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Walls;
using WifiHeat.Core.Workflow;

namespace WifiHeat.Core.Service;

/// <summary>Muro o columna de simulación tal como está en el dibujo. Longitud (o perímetro de la columna) en metros.</summary>
public sealed record WallInfo(string Handle, double LengthM, string Layer, string? MaterialId, double? ThicknessM, Reinforcement? Reinforcement = null, bool IsColumn = false);

public sealed record AccessPointInfo(string Name, Vec2 PositionM, Band Band, double FrequencyMHz, double TxPowerDbm);

/// <summary>Un problema de validación con el handle de la entidad a corregir (null si es general).</summary>
public sealed record ValidationItem(IssueSeverity Severity, string Code, string? Handle, string Message)
{
    public override string ToString() => new ValidationIssue(Severity, Code, Handle, Message).ToString();
}

/// <summary>WallCount incluye las columnas; ColumnCount dice cuántas son.</summary>
public sealed record ValidationSummary(int WallCount, int ErrorCount, int WarningCount, BoundingBox? WallBounds, IReadOnlyList<ValidationItem> Items, int ColumnCount = 0)
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
    IReadOnlyList<string> PartlyUnpaired)
{
    /// <summary>Figuras cerradas pequeñas que no se usaron como caras: posibles columnas, una por contorno distinto.</summary>
    public IReadOnlyList<string> SmallClosedFigures { get; init; } = [];
}

/// <summary>
/// Figura del plano que puede ser una columna: un hatch o una polilínea cerrada. Kind: nombre del tipo de entidad.
/// Polygon null: no se pudo leer como contorno de tramos rectos, y Error dice por qué. IsColumn: ya es una columna de simulación.
/// </summary>
public sealed record ClosedFigure(string Handle, string Kind, Polygon2? Polygon, string? Error, bool IsColumn = false);

public sealed record NewColumn(Polygon2 Footprint, string MaterialId, Reinforcement Reinforcement);

public sealed record ColumnUpdate(string Handle, string MaterialId, Reinforcement Reinforcement);

/// <summary>
/// Resultado de marcar columnas. Kept: figuras que coinciden con una columna que ya existía (se actualiza su armadura).
/// Duplicates: figuras de la misma columna dentro de la selección (un hatch y su contorno). TooLarge e Invalid no se generaron.
/// </summary>
public sealed record ColumnGeneration(
    int Created,
    int Kept,
    int Duplicates,
    Reinforcement Reinforcement,
    double AdditionalLossDb,
    IReadOnlyList<string> ColumnHandles,
    IReadOnlyList<string> TooLarge,
    IReadOnlyList<string> Invalid)
{
    public int ColumnCount => Created + Kept;
}

public sealed record LayerFaces(string Layer, IReadOnlyList<FaceSegment> Faces);

/// <summary>Eje creado antes por la generación automática, identificado por su marca en el dibujo.</summary>
public sealed record GeneratedAxis(string Handle, Segment2 Axis);

/// <summary>Eje a crear. MaterialId null = sin material.</summary>
public sealed record NewAxis(Segment2 Axis, double ThicknessM, string? MaterialId = null);

/// <summary>Línea o polilínea elegida en el dibujo, con sus tramos rectos en metros. IsWall: ya está en la capa de muros. Closed: polilínea cerrada.</summary>
public sealed record EntityFaces(string Handle, string Layer, bool IsWall, IReadOnlyList<Segment2> Segments, bool Closed = false);

/// <summary>Pares de caras de un mismo muro entre entidades que todavía no son muros. FaceHandles: las entidades emparejadas.</summary>
public sealed record FacePairSurvey(int PairCount, IReadOnlyList<string> FaceHandles, double MinThicknessM, double MaxThicknessM)
{
    public static FacePairSurvey None { get; } = new(0, [], 0, 0);

    public bool HasPairs => PairCount > 0;
}

/// <summary>
/// Resultado de asignar material uniendo caras. AxesKept: ejes que ya existían en ese lugar y recibieron el material.
/// FaceCount: entidades que quedaron como caras en su capa. SingleWalls: entidades sin par que pasaron a ser muros.
/// </summary>
public sealed record FaceJoin(
    int AxesCreated,
    int AxesKept,
    int FaceCount,
    int SingleWalls,
    double MinThicknessM,
    double MaxThicknessM,
    IReadOnlyList<string> AxisHandles,
    IReadOnlyList<string> PartlyUnpaired)
{
    public int AxisCount => AxesCreated + AxesKept;

    public int WallCount => AxisCount + SingleWalls;
}

/// <summary>Muros de simulación paralelos y cercanos, que suman la pérdida dos veces.</summary>
public sealed record ParallelWallsInfo(IReadOnlyList<string> Handles, double SeparationM, bool Convertible);

/// <summary>Resultado de convertir muros paralelos en ejes. Skipped: muros que siguen igual porque tienen tramos largos sin par.</summary>
public sealed record ParallelConversion(int AxesCreated, int FacesMoved, IReadOnlyList<string> AxisHandles, IReadOnlyList<string> Skipped);
