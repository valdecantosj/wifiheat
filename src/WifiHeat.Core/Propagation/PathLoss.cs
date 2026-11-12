namespace WifiHeat.Core.Propagation;

/// <summary>Pérdida en el espacio libre: L_0 = 32,45 + 20·log10 f[MHz] + 20·log10 d[km].</summary>
public static class PathLoss
{
    /// <summary>Distancia mínima: evita L_0 infinita en la posición del AP.</summary>
    public const double MinDistanceM = 1.0;

    public static double FreeSpaceDb(double frequencyMHz, double distanceKm)
    {
        if (frequencyMHz <= 0) throw new ArgumentOutOfRangeException(nameof(frequencyMHz));
        if (distanceKm <= 0) throw new ArgumentOutOfRangeException(nameof(distanceKm));
        return 32.45 + 20 * Math.Log10(frequencyMHz) + 20 * Math.Log10(distanceKm);
    }

    public static double EffectiveDistanceM(double distanceM) => Math.Max(distanceM, MinDistanceM);

    /// <summary>L_0 para una distancia en metros, aplicando la distancia mínima.</summary>
    public static double FreeSpaceDbFromMetres(double frequencyMHz, double distanceM) =>
        FreeSpaceDb(frequencyMHz, EffectiveDistanceM(distanceM) / 1000.0);
}
