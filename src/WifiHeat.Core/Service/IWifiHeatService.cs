using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
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

    /// <summary>
    /// Pares de caras de un mismo muro entre las entidades elegidas que todavía no son muros,
    /// con las reglas de la generación de ejes. No modifica el dibujo.
    /// </summary>
    FacePairSurvey FindFacePairs(IReadOnlyCollection<string> handles);

    /// <summary>
    /// Como <see cref="AssignMaterial"/>, pero cada par de caras se reemplaza por un eje con el material elegido y el espesor medido.
    /// Las caras quedan en su capa; solo las entidades sin par pasan a la capa de muros.
    /// </summary>
    OperationResult<FaceJoin> AssignMaterialJoiningFaces(IReadOnlyCollection<string> handles, string materialId, double? thicknessM);

    /// <summary>Material de las columnas y adicional de pérdida por nivel de armadura.</summary>
    ColumnReinforcement ColumnReinforcement { get; }

    /// <summary>
    /// Marca como columnas de hormigón armado las figuras cerradas elegidas (hatches, polilíneas cerradas o columnas ya creadas).
    /// Crea una polilínea cerrada por columna en la capa de muros, sin tocar las figuras originales. Un hatch y su contorno dan una sola
    /// columna, y repetir la acción no duplica. Las figuras de más de 1,5 m de lado y las que tienen arcos o islas no se generan.
    /// </summary>
    OperationResult<ColumnGeneration> AssignColumns(IReadOnlyCollection<string> handles, Reinforcement reinforcement);

    /// <summary>Muros de simulación paralelos y cercanos (las dos caras de un mismo muro), que suman la pérdida dos veces.</summary>
    IReadOnlyList<ParallelWallsInfo> FindParallelWalls();

    /// <summary>Reemplaza cada grupo de muros paralelos por un eje con su material y pasa las caras a una capa que no se lee como muro.</summary>
    OperationResult<ParallelConversion> ConvertParallelWalls();

    /// <summary>Capas con líneas o polilíneas enteras dentro de la zona de la planta, con la cantidad de segmentos y de ejes de cada una.</summary>
    OperationResult<AxisSurvey> SurveyAxisLayers(BoundingBox windowM, double mergeGapM = AxisDefaults.MergeGapM);

    /// <summary>
    /// Crea ejes de muro a partir de las caras de las capas elegidas. Los ejes generados antes dentro de la zona
    /// se reemplazan; los que coinciden se conservan con su material.
    /// </summary>
    OperationResult<AxisGeneration> GenerateAxes(BoundingBox windowM, IReadOnlyCollection<string> layers, double mergeGapM = AxisDefaults.MergeGapM);

    /// <summary>
    /// Crea ejes de muro aplicando el mismo detector solo a las líneas y polilíneas elegidas, sin importar su capa.
    /// No borra nada: si ya hay un eje en ese lugar se conserva. Los muros de simulación elegidos no se usan como caras.
    /// </summary>
    OperationResult<AxisGeneration> GenerateAxesFromSelection(IReadOnlyCollection<string> handles, double mergeGapM = AxisDefaults.MergeGapM);

    OperationResult<AccessPointInfo> SetAccessPoint(Vec2 positionM);

    OperationResult<SimulationSettings> Configure(SimulationSettings settings);

    CoverageAreaInfo GetCoverageArea();

    /// <summary>Usa como área de cobertura la polilínea cerrada con ese handle.</summary>
    OperationResult<CoverageAreaInfo> SetCoverageArea(string polylineHandle);

    /// <summary>Vuelve al rectángulo de los muros.</summary>
    OperationResult<CoverageAreaInfo> ClearCoverageArea();

    OperationResult<CalculationSummary> Calculate();

    /// <summary>
    /// Reemplaza el mapa del dibujo por la pérdida en dB que agrega la armadura de las columnas (diferencia con el cálculo
    /// que las toma como hormigón sin armadura). No cambia el cálculo ni la configuración: la resolución es la de la sesión o 0,20 m, la menor.
    /// Volver al mapa de cobertura es ejecutar <see cref="Calculate"/>.
    /// </summary>
    OperationResult<ReinforcementEffectSummary> ShowReinforcementEffect();

    /// <summary>Leyenda del mapa de efecto de la armadura mientras el dibujo lo muestra; null si muestra el mapa de cobertura.</summary>
    Legend? ReinforcementEffectLegend { get; }

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
