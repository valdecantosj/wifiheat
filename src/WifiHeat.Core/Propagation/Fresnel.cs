using System.Numerics;

namespace WifiHeat.Core.Propagation;

/// <summary>
/// Reflexión en la interfaz aire–material para polarización TE (campo eléctrico perpendicular al plano de incidencia,
/// es decir paralelo a una pared vertical con antenas verticales). ITU-R P.2040, ec. 31a, con el medio 1 en aire
/// y la ley de Snell: Γ = (cos θ − √(ε − sin²θ)) / (cos θ + √(ε − sin²θ)), con θ medido desde la normal.
/// </summary>
public static class Fresnel
{
    public static Complex ReflectionTe(Complex permittivity, double cosIncidence)
    {
        double cos = Math.Clamp(cosIncidence, 0.0, 1.0);
        Complex root = Complex.Sqrt(permittivity - (1 - cos * cos));
        return (cos - root) / (cos + root);
    }

    /// <summary>Pérdida por reflexión −20·log10 |Γ| (dB, ≥ 0).</summary>
    public static double ReflectionLossDb(Complex permittivity, double cosIncidence)
    {
        double magnitude = ReflectionTe(permittivity, cosIncidence).Magnitude;
        return magnitude > 0 ? Math.Max(0.0, -20 * Math.Log10(magnitude)) : double.PositiveInfinity;
    }

    public static double ReflectionLossDb(Dielectric material, double frequencyMHz, double cosIncidence) =>
        ReflectionLossDb(material.ComplexPermittivity(frequencyMHz / 1000), cosIncidence);
}
