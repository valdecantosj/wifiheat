using WifiHeat.Core.Geometry;

namespace WifiHeat.Core.Simulation;

/// <summary>
/// Muro de simulación: un eje formado por uno o más segmentos (metros).
/// Material y espesor pueden faltar al leerse del DWG; la validación lo informa como FALTA PROPIEDAD.
/// El espesor se registra pero no modifica la pérdida. El conteo de cruces está en <see cref="PathCrossings"/>.
/// </summary>
public sealed record Wall(string Id, IReadOnlyList<Segment2> Segments, string? MaterialId, double? ThicknessM);
