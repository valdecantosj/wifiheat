namespace WifiHeat.Core.Simulation;

/// <summary>
/// Un intervalo de la escala: desde MinDbm (inclusive) hasta el nivel anterior (exclusivo).
/// AnchorDbm es el valor donde la rampa continua toma exactamente este color.
/// </summary>
public sealed record SignalLevel(double MinDbm, double AnchorDbm, string Label, string ColorName, byte R, byte G, byte B);

public readonly record struct Rgb(byte R, byte G, byte B);

/// <summary>
/// Escala de colores orientativa: no son umbrales universales de calidad ni de conexión.
/// La leyenda y la explicación usan las clases; el mapa usa la rampa continua anclada en el centro de cada clase.
/// </summary>
public static class SignalScale
{
    public static IReadOnlyList<SignalLevel> Levels { get; } =
    [
        new(-50, -50, "≥ −50 dBm", "Verde oscuro", 0, 128, 0),
        new(-60, -55, "−60 a −50 dBm", "Verde claro", 120, 200, 80),
        new(-67, -63.5, "−67 a −60 dBm", "Amarillo verdoso", 190, 220, 40),
        new(-75, -71, "−75 a −67 dBm", "Amarillo", 255, 220, 0),
        new(-85, -80, "−85 a −75 dBm", "Naranja", 255, 140, 0),
        new(double.NegativeInfinity, -90, "< −85 dBm", "Rojo", 220, 30, 30),
    ];

    public static SignalLevel Classify(double dbm) => Levels[IndexOf(dbm)];

    public static int IndexOf(double dbm)
    {
        for (int i = 0; i < Levels.Count; i++)
        {
            if (dbm >= Levels[i].MinDbm) return i;
        }

        return Levels.Count - 1;
    }

    /// <summary>
    /// Color continuo: interpolación lineal en RGB entre los anclajes de colores vecinos.
    /// Por encima del anclaje del verde oscuro y por debajo del anclaje del rojo el color es constante.
    /// </summary>
    public static Rgb RampColor(double dbm)
    {
        if (double.IsNaN(dbm)) throw new ArgumentException("El valor no es un número.", nameof(dbm));

        if (dbm >= Levels[0].AnchorDbm) return ColorOf(0);
        for (int i = 1; i < Levels.Count; i++)
        {
            double upper = Levels[i - 1].AnchorDbm;
            double lower = Levels[i].AnchorDbm;
            if (dbm < lower) continue;

            double t = (dbm - lower) / (upper - lower);
            return Lerp(ColorOf(i), ColorOf(i - 1), t);
        }

        return ColorOf(Levels.Count - 1);
    }

    private static Rgb ColorOf(int index) => new(Levels[index].R, Levels[index].G, Levels[index].B);

    private static Rgb Lerp(Rgb from, Rgb to, double t) => new(Mix(from.R, to.R, t), Mix(from.G, to.G, t), Mix(from.B, to.B, t));

    private static byte Mix(byte from, byte to, double t) => (byte)Math.Round(from + (to - from) * t, MidpointRounding.AwayFromZero);
}
