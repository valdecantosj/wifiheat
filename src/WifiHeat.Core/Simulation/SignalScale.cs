namespace WifiHeat.Core.Simulation;

/// <summary>Un intervalo de la escala: desde MinDbm (inclusive) hasta el nivel anterior (exclusivo).</summary>
public sealed record SignalLevel(double MinDbm, string Label, string ColorName, byte R, byte G, byte B);

public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// Escala de colores orientativa: no son umbrales universales de calidad ni de conexión.
/// La leyenda y la explicación usan las clases; el mapa usa la rampa continua anclada en los mismos cortes.
/// </summary>
public static class SignalScale
{
    /// <summary>Anclaje del rojo en la rampa: un intervalo de 10 dB por debajo del último corte.</summary>
    public const double RedAnchorDbm = -95;

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

    /// <summary>
    /// Color continuo: interpolación lineal en RGB entre colores vecinos de la escala.
    /// Cada color queda en el corte inferior de su clase (el rojo en <see cref="RedAnchorDbm"/>);
    /// por encima de −50 dBm y por debajo del rojo el color es constante.
    /// </summary>
    public static Rgb RampColor(double dbm)
    {
        if (double.IsNaN(dbm)) throw new ArgumentException("El valor no es un número.", nameof(dbm));

        if (dbm >= AnchorOf(0)) return ColorOf(0);
        for (int i = 1; i < Levels.Count; i++)
        {
            double upper = AnchorOf(i - 1);
            double lower = AnchorOf(i);
            if (dbm < lower) continue;

            double t = (dbm - lower) / (upper - lower);
            return Lerp(ColorOf(i), ColorOf(i - 1), t);
        }

        return ColorOf(Levels.Count - 1);
    }

    private static double AnchorOf(int index) => index == Levels.Count - 1 ? RedAnchorDbm : Levels[index].MinDbm;

    private static Rgb ColorOf(int index) => new(Levels[index].R, Levels[index].G, Levels[index].B);

    private static Rgb Lerp(Rgb from, Rgb to, double t) => new(Mix(from.R, to.R, t), Mix(from.G, to.G, t), Mix(from.B, to.B, t));

    private static byte Mix(byte from, byte to, double t) => (byte)Math.Round(from + (to - from) * t, MidpointRounding.AwayFromZero);
}
