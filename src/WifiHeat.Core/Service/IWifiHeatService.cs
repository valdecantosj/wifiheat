using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Workflow;

namespace WifiHeat.Core.Service;

/// <summary>
/// Operaciones del flujo de simulación de un documento. Comandos y panel usan esta misma interfaz.
/// Puntos y longitudes en metros, coordenadas universales.
/// </summary>
public interface IWifiHeatService
{
    SimulationSettings Settings { get; }

    AccessPointInfo? AccessPoint { get; }

    IReadOnlyList<Material> Materials { get; }

    /// <summary>True si el último cálculo sigue vigente (no cambiaron muros, AP, área ni parámetros).</summary>
    bool HasResult { get; }

    string SuggestedExportPath { get; }

    /// <summary>Resumen del último cálculo hecho en esta sesión, aunque ya no esté vigente; null si no hubo ninguno.</summary>
    CalculationSummary? LastCalculation { get; }

    DrawingSetup GetDrawingSetup();

    /// <summary>Guarda la unidad del dibujo y crea las capas propias si faltan.</summary>
    OperationResult<DrawingUnit> SetDrawingUnit(DrawingUnit unit);

    /// <summary>Lee los muros del dibujo (el dibujo es la fuente de verdad) y los deja validados en la sesión.</summary>
    IReadOnlyList<WallInfo> LoadWalls();

    ValidationSummary Validate();

    /// <summary>Asigna material y espesor. Con espesor null se conserva el de cada muro (o el valor por defecto si no tiene).</summary>
    OperationResult<int> AssignMaterial(IReadOnlyCollection<string> handles, string materialId, double? thicknessM);

    /// <summary>Capas con líneas o polilíneas enteras dentro de la ventana, con la cantidad de segmentos y de ejes de cada una.</summary>
    OperationResult<AxisSurvey> SurveyAxisLayers(BoundingBox windowM, double mergeGapM = AxisDefaults.MergeGapM);

    /// <summary>
    /// Crea ejes de muro a partir de las caras de las capas elegidas. Los ejes generados antes dentro de la ventana
    /// se reemplazan; los que coinciden se conservan con su material.
    /// </summary>
    OperationResult<AxisGeneration> GenerateAxes(BoundingBox windowM, IReadOnlyCollection<string> layers, double mergeGapM = AxisDefaults.MergeGapM);

    OperationResult<AccessPointInfo> SetAccessPoint(Vec2 positionM);

    OperationResult<SimulationSettings> Configure(SimulationSettings settings);

    CoverageAreaInfo GetCoverageArea();

    /// <summary>Usa como área de cobertura la polilínea cerrada con ese handle.</summary>
    OperationResult<CoverageAreaInfo> SetCoverageArea(string polylineHandle);

    /// <summary>Vuelve al rectángulo de los muros.</summary>
    OperationResult<CoverageAreaInfo> ClearCoverageArea();

    OperationResult<CalculationSummary> Calculate();

    /// <summary>Cambia la atenuación del mapa ya dibujado, sin recalcular.</summary>
    OperationResult<int> SetOverlayFade(int percent);

    OperationResult<QueryResult> Query(Vec2 pointM);

    /// <summary>Exporta el último cálculo. Devuelve la ruta completa escrita.</summary>
    OperationResult<string> ExportPng(string path);
}

public static class AxisDefaults
{
    public const double MergeGapM = Walls.AxisDetectionOptions.DefaultMergeGapM;

    public const string PreferredLayer = "muros";

    /// <summary>Capas que no se procesan salvo que el usuario las elija.</summary>
    public static IReadOnlyList<string> ExplicitOnlyLayers { get; } = ["0", "ventanas"];

    public static bool IsExplicitOnly(string layer) => ExplicitOnlyLayers.Contains(layer, StringComparer.OrdinalIgnoreCase);
}
