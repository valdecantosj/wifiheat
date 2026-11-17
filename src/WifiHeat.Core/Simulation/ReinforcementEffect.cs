using System.Globalization;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;

namespace WifiHeat.Core.Simulation;

/// <summary>
/// Diferencia en dB entre el cálculo normal y el mismo cálculo con las columnas como hormigón sin armadura.
/// Grid: la diferencia por celda; las celdas cuyo trayecto no suma adicional de armadura valen NaN (sin valor).
/// </summary>
public sealed record ReinforcementEffectMap(RssiGrid Grid, int AffectedCells, double MaxDb)
{
    public bool HasEffect => AffectedCells > 0;
}

public static class ReinforcementEffect
{
    /// <summary>Resolución máxima (celda más grande permitida) mientras se genera este mapa.</summary>
    public const double MaxCellSizeM = 0.20;

    /// <summary>Por debajo de este valor la celda se toma como sin efecto.</summary>
    public const double MinEffectDb = 1e-6;

    /// <summary>Celda con la que se genera el mapa: la de la sesión, sin pasar de <see cref="MaxCellSizeM"/>.</summary>
    public static double CellSizeFor(double sessionCellSizeM) => Math.Min(sessionCellSizeM, MaxCellSizeM);

    /// <summary>
    /// Pérdida que agrega la armadura en un trayecto: pérdida con armadura menos pérdida sin armadura, con la misma regla de cruces.
    /// Es igual a la diferencia de RSSI entre el cálculo sin armadura y el normal.
    /// </summary>
    public static double AddedLossDb(SimulationScene scene, SimulationScene withoutReinforcement, Segment2 path, Band band) =>
        PathCrossings.TotalLossDb(scene, path, band) - PathCrossings.TotalLossDb(withoutReinforcement, path, band);

    /// <summary>Null si ninguna columna tiene adicional de armadura: no hay nada que mostrar.</summary>
    public static ReinforcementEffectMap? Calculate(SimulationScene scene, AccessPoint ap, RadioSettings settings, GridSpec spec, Polygon2 area)
    {
        if (!scene.HasReinforcement) return null;

        SimulationScene plain = scene.WithoutReinforcement();
        var values = new double[spec.Columns, spec.Rows];
        Parallel.For(0, spec.Columns, column =>
        {
            for (int row = 0; row < spec.Rows; row++)
            {
                Vec2 min = spec.CellMin(column, row);
                var cell = new BoundingBox(min, new Vec2(min.X + spec.CellSizeM, min.Y + spec.CellSizeM));
                values[column, row] = double.NaN;
                if (!area.Overlaps(cell)) continue;

                double added = AddedLossDb(scene, plain, new Segment2(ap.Position, spec.CellCenter(column, row)), settings.Band);
                if (added > MinEffectDb) values[column, row] = added;
            }
        });

        var grid = new RssiGrid(spec, values, area);
        return new ReinforcementEffectMap(grid, grid.ValueCount, grid.ValueCount > 0 ? grid.Max : 0);
    }
}

/// <summary>
/// Rampa del mapa de efecto de la armadura: cinco tramos iguales entre 0 y el máximo (redondeado hacia arriba a un múltiplo de 2,5 dB),
/// de amarillo claro a rojo oscuro. Cada color está anclado en el centro de su tramo. Para la leyenda se reutiliza
/// <see cref="SignalLevel"/>: MinDbm guarda aquí el límite inferior del tramo en dB y AnchorDbm su centro.
/// </summary>
public sealed class EffectScale
{
    public const int Steps = 5;

    /// <summary>Por debajo de este valor (después de interpolar) el píxel no se pinta.</summary>
    public const double VisibleThresholdDb = 0.05;

    private const double RoundingDb = 2.5;

    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    private static readonly Rgb[] Colors =
    [
        new(255, 245, 157),
        new(255, 202, 74),
        new(250, 130, 40),
        new(205, 45, 30),
        new(120, 10, 15),
    ];

    private static readonly string[] ColorNames = ["Amarillo claro", "Amarillo", "Naranja", "Rojo", "Rojo oscuro"];

    public EffectScale(double maxDb)
    {
        if (!double.IsFinite(maxDb) || maxDb <= 0) throw new ArgumentOutOfRangeException(nameof(maxDb));

        TopDb = Math.Ceiling(maxDb / RoundingDb - 1e-9) * RoundingDb;
    }

    /// <summary>Extremo superior de la rampa, en dB.</summary>
    public double TopDb { get; }

    public double StepDb => TopDb / Steps;

    public Rgb Color(double db)
    {
        double position = db / StepDb - 0.5;
        if (position <= 0) return Colors[0];
        if (position >= Steps - 1) return Colors[Steps - 1];

        int i = (int)Math.Floor(position);
        double t = position - i;
        Rgb a = Colors[i], b = Colors[i + 1];
        return new Rgb(Mix(a.R, b.R, t), Mix(a.G, b.G, t), Mix(a.B, b.B, t));
    }

    /// <summary>Tramos de la leyenda, del mayor al menor.</summary>
    public IReadOnlyList<SignalLevel> Levels()
    {
        var levels = new List<SignalLevel>(Steps);
        for (int i = Steps - 1; i >= 0; i--)
        {
            double from = i * StepDb, to = (i + 1) * StepDb;
            Rgb c = Colors[i];
            levels.Add(new SignalLevel(from, from + StepDb / 2, string.Create(Es, $"{from:0.##} a {to:0.##} dB"), ColorNames[i], c.R, c.G, c.B));
        }

        return levels;
    }

    private static byte Mix(byte from, byte to, double t) => (byte)Math.Round(from + (to - from) * t, MidpointRounding.AwayFromZero);
}
