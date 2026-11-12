using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
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

public sealed record CoverageAreaRead(Polygon2? Polygon, string Description, string? Warning);

public sealed record WallAssignment(string Handle, string MaterialId, double ThicknessM);

public sealed record OverlayOutcome(string? ImagePath, IReadOnlyList<string> Warnings);
