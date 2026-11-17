using WifiHeat.Core.Geometry;

namespace WifiHeat.Core.Simulation;

public enum PathKind
{
    /// <summary>Recta AP–punto.</summary>
    Direct,

    /// <summary>Polilínea AP → vértices → punto que rodea uno o más vértices.</summary>
    Diffracted,
}

/// <summary>
/// Trayecto que aporta potencia en un punto.
/// Vertices: vértices rodeados, en el orden del trayecto (vacío en el directo). LengthM: longitud total.
/// LossDb: pérdida total (L_0 con la longitud del trayecto, materiales y difracción). PowerShare: fracción (0 a 1) de la potencia sumada.
/// </summary>
public sealed record PathContribution(PathKind Kind, IReadOnlyList<Vec2> Vertices, double LengthM, double LossDb, double PowerShare)
{
    public int Diffractions => Vertices.Count;
}
