namespace WifiHeat.Core.Simulation;

/// <summary>Un intervalo de la escala: desde MinDbm (inclusive) hasta el nivel anterior (exclusivo).</summary>
public sealed record SignalLevel(double MinDbm, string Label, string ColorName, byte R, byte G, byte B);

/// <summary>
/// Escala de colores orientativa: no son umbrales universales de calidad ni de conexión.
/// Mapa y leyenda usan exactamente estos cortes.
/// </summary>
public static class SignalScale
{
    public static IReadOnlyList<SignalLevel> Levels { get; } =
    [
        new(-50, "≥ −50 dBm", "Verde oscuro", 0, 128, 0),
        new(-60, "−60 a −50 dBm", "Verde claro", 120, 200, 80),
        new(-67, "−67 a −60 dBm", "Amarillo verdoso", 190, 220, 40),
        new(-75, "−75 a −67 dBm", "Amarillo", 255, 220, 0),
        new(-85, "−85 a −75 dBm", "Naranja", 255, 140, 0),
        new(double.NegativeInfinity, "< −85 dBm", "Rojo", 220, 30, 30),
    ];

    public static SignalLevel Classify(double dbm)
    {
        foreach (SignalLevel level in Levels)
        {
            if (dbm >= level.MinDbm)
            {
                return level;
            }
        }

        return Levels[^1];
    }

    public static int IndexOf(double dbm)
    {
        for (int i = 0; i < Levels.Count; i++)
        {
            if (dbm >= Levels[i].MinDbm) return i;
        }

        return Levels.Count - 1;
    }
}
