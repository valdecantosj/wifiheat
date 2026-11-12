using System.Globalization;
using System.Text;
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
        sb.AppendLine(F($"d = {c.DistanceM:0.00} m{(c.EffectiveDistanceM > c.DistanceM ? F($" (se usa el mínimo de {c.EffectiveDistanceM:0.##} m)") : "")}"));
        sb.AppendLine(F($"L_0 = {c.PathLossDb:0.00} dB"));
        sb.AppendLine(F($"Muros cruzados: {c.Walls.Count}"));
        foreach (WallCrossing w in c.Walls)
        {
            sb.AppendLine(F($"  - Muro {w.WallId} · {w.MaterialName}: {w.LossDb:0.0} dB"));
        }

        sb.AppendLine(F($"L_materiales = {c.WallLossDb:0.00} dB"));
        SignalLevel level = SignalScale.Classify(c.ReceivedPowerDbm);
        sb.AppendLine(F($"P_RX = RSSI estimado = {c.ReceivedPowerDbm:0.00} dBm → {level.ColorName} ({level.Label})"));
        sb.Append("Estimación del modelo; no es una medición.");
        return sb.ToString();
    }

    private static string F(FormattableString text) => text.ToString(Es);
}
