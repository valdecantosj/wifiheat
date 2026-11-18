using System.Numerics;

namespace WifiHeat.Core.Propagation;

/// <summary>
/// Propiedades eléctricas de un material (ITU-R P.2040, Tabla 3): ε′ = a·f^b y σ = c·f^d [S/m], con f en GHz.
/// </summary>
public sealed record Dielectric(double A, double B, double C, double D)
{
    /// <summary>Constante de ε″ = 17,98·σ / f[GHz] (P.2040, ec. 59).</summary>
    public const double ImaginaryFactor = 17.98;

    public double RelativePermittivity(double frequencyGHz) => A * Math.Pow(frequencyGHz, B);

    public double ConductivitySPerM(double frequencyGHz) => C * Math.Pow(frequencyGHz, D);

    /// <summary>Permitividad relativa compleja ε′ − j·ε″.</summary>
    public Complex ComplexPermittivity(double frequencyGHz) =>
        new(RelativePermittivity(frequencyGHz), -ImaginaryFactor * ConductivitySPerM(frequencyGHz) / frequencyGHz);
}
