using System.Globalization;
using System.Text;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;

namespace WifiHeat.Core.Workflow;

/// <summary>Texto explicativo de una consulta, igual para consola y panel.</summary>
public static class Explanation
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    public static string Format(PointCalculation c)
    {
        var s = c.Settings;
        var sb = new StringBuilder();
        sb.AppendLine(F($"Consulta en {c.Point} (punto exacto)"));
        sb.AppendLine(F($"{c.AccessPoint.Name} en {c.AccessPoint.Position} · Banda {BandInfo.Label(s.Band)} ({s.FrequencyMHz:0} MHz)"));
        sb.AppendLine(F($"P_TX = {s.TxPowerDbm:0.#} dBm · L_cable,TX = {s.TxCableLossDb:0.#} dB · G_antena,TX = {s.TxAntennaGainDbi:0.#} dBi · G_antena,RX = {s.RxAntennaGainDbi:0.#} dBi · L_cable,RX = {s.RxCableLossDb:0.#} dB"));
        bool withPaths = c.Model != PropagationModel.StraightLine && c.Paths is not null;
        if (withPaths)
        {
            sb.AppendLine($"Modelo: {PropagationModelInfo.Label(c.Model)}");
            sb.AppendLine("Recta AP–punto:");
        }

        sb.AppendLine(F($"d = {c.DistanceM:0.00} m{(c.EffectiveDistanceM > c.DistanceM ? F($" (se usa el mínimo de {c.EffectiveDistanceM:0.##} m)") : "")}"));
        sb.AppendLine(F($"L_0 = {c.PathLossDb:0.00} dB"));
        sb.AppendLine(F($"{(c.Walls.Any(w => w.IsColumn) ? "Muros y columnas cruzados" : "Muros cruzados")}: {c.Walls.Count}"));
        foreach (WallCrossing w in c.Walls)
        {
            string junction = w.JunctionWith.Count == 0
                ? ""
                : $" (unión con {string.Join(", ", w.JunctionWith)}: se cuenta una vez la pérdida mayor)";
            sb.AppendLine(w.Reinforcement is { } armour
                ? F($"  - Columna {w.WallId} · {w.MaterialName} con armadura {ReinforcementName(armour)}: {w.LossDb:0.0} dB ({w.LossDb - w.ReinforcementLossDb:0.0} + {w.ReinforcementLossDb:0.0} de armadura){junction}")
                : F($"  - Muro {w.WallId} · {w.MaterialName}: {w.LossDb:0.0} dB{junction}"));
        }

        sb.AppendLine(F($"L_materiales = {c.WallLossDb:0.00} dB"));
        if (withPaths) AppendPaths(sb, c.Paths!);
        if (c.Model == PropagationModel.DiffractionAndReflection) AppendReflections(sb, Reflections(c));
        SignalLevel level = SignalScale.Classify(c.ReceivedPowerDbm);
        sb.AppendLine(F($"P_RX = RSSI estimado = {c.ReceivedPowerDbm:0.00} dBm → {level.ColorName} ({level.Label})"));
        sb.Append("Estimación del modelo; no es una medición.");
        return sb.ToString();
    }

    /// <summary>Cantidad de trayectos que se listan; el resto se resume en una línea.</summary>
    public const int MaxListedPaths = 5;

    private static void AppendPaths(StringBuilder sb, IReadOnlyList<PathContribution> paths)
    {
        sb.AppendLine(F($"Trayectos sumados: {paths.Count} (suma de potencias, sin fases)"));
        for (int i = 0; i < Math.Min(paths.Count, MaxListedPaths); i++)
        {
            PathContribution p = paths[i];
            sb.AppendLine(F($"  {i + 1}. {DescribePath(p)} · {p.LengthM:0.00} m · pérdida {p.LossDb:0.0} dB · {p.PowerShare * 100:0.0} % de la potencia"));
        }

        if (paths.Count > MaxListedPaths)
        {
            double rest = paths.Skip(MaxListedPaths).Sum(p => p.PowerShare);
            sb.AppendLine(F($"  Los otros {paths.Count - MaxListedPaths} aportan el {rest * 100:0.0} %."));
        }
    }

    /// <summary>Trayectos reflejados sumados en la consulta, del que más aporta al que menos; vacío sin reflexiones.</summary>
    public static IReadOnlyList<PathContribution> Reflections(PointCalculation c) =>
        [.. (c.Paths ?? []).Where(p => p.Kind == PathKind.Reflected && p.Reflection is not null).OrderByDescending(p => p.PowerShare)];

    /// <summary>«Muro W1» o «Columna C1».</summary>
    public static string ObstacleName(PathReflection r) => $"{(r.IsColumn ? "Columna" : "Muro")} {r.WallId}";

    /// <summary>Todos los rebotes sumados (la lista de trayectos muestra solo los que más aportan).</summary>
    private static void AppendReflections(StringBuilder sb, IReadOnlyList<PathContribution> reflections)
    {
        sb.AppendLine(reflections.Count == 0
            ? "Muros donde rebota: ninguno (ningún rebote llega al punto o todos quedan debajo de la poda)"
            : $"Muros donde rebota: {reflections.Count}");
        foreach (PathContribution p in reflections)
        {
            PathReflection r = p.Reflection!;
            Vec2 at = p.Vertices[0];
            sb.AppendLine(F($"  - {ObstacleName(r)} · {r.MaterialName} en ({at.X:0.##}; {at.Y:0.##}): {r.IncidenceDeg:0}° desde la normal, rebote {r.LossDb:0.0} dB, trayecto {p.LengthM:0.00} m y {p.LossDb:0.0} dB, {p.PowerShare * 100:0.0} % de la potencia"));
        }
    }

    /// <summary>«Directo», «Difractado ×2 en (0; 0) y (0,3; 0)» o «Reflejado en muro M1 en (2; 0) · 35° · rebote 6,4 dB».</summary>
    public static string DescribePath(PathContribution path)
    {
        if (path.Kind == PathKind.Direct) return "Directo";
        if (path.Kind == PathKind.Reflected && path.Reflection is { } r && path.Vertices.Count > 0)
        {
            Vec2 at = path.Vertices[0];
            return F($"Reflejado en {(r.IsColumn ? "columna" : "muro")} {r.WallId} en ({at.X:0.##}; {at.Y:0.##}) · {r.IncidenceDeg:0}° · rebote {r.LossDb:0.0} dB");
        }

        string vertices = string.Join(" y ", path.Vertices.Select(v => F($"({v.X:0.##}; {v.Y:0.##})")));
        return F($"Difractado ×{path.Diffractions} en {vertices}");
    }

    public static string ReinforcementName(Reinforcement level) =>
        ColumnReinforcement.Default.Get(level).Name.ToLower(Es);

    private static string F(FormattableString text) => text.ToString(Es);
}
