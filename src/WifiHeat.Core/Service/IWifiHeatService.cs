using WifiHeat.Core.Geometry;
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

    /// <summary>Lee los muros del dibujo (el dibujo es la fuente de verdad) y los deja validados en la sesión.</summary>
    IReadOnlyList<WallInfo> LoadWalls();

    ValidationSummary Validate();

    /// <summary>Asigna material y espesor. Con espesor null se conserva el de cada muro (o el valor por defecto si no tiene).</summary>
    OperationResult<int> AssignMaterial(IReadOnlyCollection<string> handles, string materialId, double? thicknessM);

    OperationResult<AccessPointInfo> SetAccessPoint(Vec2 positionM);

    OperationResult<SimulationSettings> Configure(SimulationSettings settings);

    OperationResult<CalculationSummary> Calculate();

    OperationResult<QueryResult> Query(Vec2 pointM);

    /// <summary>Exporta el último cálculo. Devuelve la ruta completa escrita.</summary>
    OperationResult<string> ExportPng(string path);
}
