namespace WifiHeat.Core.Propagation;

/// <summary>Difracción por filo de cuchilla (ITU-R P.526, §4.1).</summary>
public static class KnifeEdge
{
    /// <summary>Por debajo de este ν la pérdida vale 0 dB.</summary>
    public const double CutoffNu = -0.78;

    /// <summary>λ [m] = 299,792458 / f [MHz].</summary>
    public const double SpeedOfLightMPerMicrosecond = 299.792458;

    /// <summary>J(0) = 6,03 dB: pérdida con el borde justo sobre la línea de vista.</summary>
    public static readonly double J0 = 6.9 + 20 * Math.Log10(Math.Sqrt(0.1 * 0.1 + 1) - 0.1);

    /// <summary>J(ν) = 6,9 + 20·log10(√((ν − 0,1)² + 1) + ν − 0,1), en dB; 0 para ν ≤ −0,78.</summary>
    public static double LossDb(double nu)
    {
        if (nu <= CutoffNu) return 0.0;
        return 6.9 + 20 * Math.Log10(Math.Sqrt((nu - 0.1) * (nu - 0.1) + 1) + nu - 0.1);
    }

    /// <summary>ν = θ·√(2·d1·d2 / (λ·(d1 + d2))), con θ en radianes y las distancias y λ en metros.</summary>
    public static double NuFromAngle(double thetaRad, double d1M, double d2M, double wavelengthM) =>
        thetaRad * Math.Sqrt(2 * d1M * d2M / (wavelengthM * (d1M + d2M)));

    public static double WavelengthM(double frequencyMHz) => SpeedOfLightMPerMicrosecond / frequencyMHz;
}
