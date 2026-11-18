using WifiHeat.Core.Geometry;

namespace WifiHeat.Core.Simulation;

public enum PathKind
{
    /// <summary>Recta AP–punto.</summary>
    Direct,

    /// <summary>Polilínea AP → vértices → punto que rodea uno o más vértices.</summary>
    Diffracted,

    /// <summary>AP → punto de rebote → punto, con un rebote en un muro o una cara de columna.</summary>
    Reflected,
}

/// <summary>Rebote de un trayecto reflejado. LossDb: pérdida por reflexión −20·log10 |Γ|. IncidenceDeg: ángulo desde la normal.</summary>
public sealed record PathReflection(string WallId, bool IsColumn, double LossDb, double IncidenceDeg);

/// <summary>
/// Trayecto que aporta potencia en un punto.
/// Vertices: vértices rodeados, en el orden del trayecto (vacío en el directo), o el punto de rebote en el reflejado.
/// LengthM: longitud total. LossDb: pérdida total (L_0 con la longitud del trayecto, materiales, difracción y rebote).
/// PowerShare: fracción (0 a 1) de la potencia sumada. Reflection: datos del rebote, solo en el reflejado.
/// </summary>
public sealed record PathContribution(PathKind Kind, IReadOnlyList<Vec2> Vertices, double LengthM, double LossDb, double PowerShare,
    PathReflection? Reflection = null)
{
    public int Diffractions => Kind == PathKind.Diffracted ? Vertices.Count : 0;
}
