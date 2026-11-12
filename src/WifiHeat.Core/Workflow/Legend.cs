using System.Globalization;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;

namespace WifiHeat.Core.Workflow;

/// <summary>Contenido de la leyenda, igual para el dibujo y para la imagen exportada.</summary>
public sealed record Legend(string Title, IReadOnlyList<string> Details, IReadOnlyList<SignalLevel> Levels)
{
    private static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    public int RowCount => 1 + Details.Count + Levels.Count;

    public static Legend For(RadioSettings settings) => new(
        "RSSI estimado",
        [
            string.Create(Es, $"Banda {BandInfo.Label(settings.Band)} ({settings.FrequencyMHz:0} MHz)"),
            string.Create(Es, $"P_TX = {settings.TxPowerDbm:0.#} dBm"),
        ],
        SignalScale.Levels);

    /// <summary>Alto de fila proporcional al tamaño del mapa, con un mínimo legible.</summary>
    public static double RowHeightM(double mapExtentM) => Math.Max(mapExtentM / 30.0, 0.2);
}
