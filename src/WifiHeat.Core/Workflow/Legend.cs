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

    public static Legend For(RadioSettings settings, PropagationModel model = PropagationModel.StraightLine) => new(
        "RSSI estimado",
        [
            string.Create(Es, $"Banda {BandInfo.Label(settings.Band)} ({settings.FrequencyMHz:0} MHz)"),
            string.Create(Es, $"P_TX = {settings.TxPowerDbm:0.#} dBm"),
            ModelLine(model),
        ],
        SignalScale.Levels);

    public static string ModelLine(PropagationModel model) => $"Modelo: {PropagationModelInfo.Label(model)}";

    public const string ReinforcementEffectTitle = "Pérdida que agrega la armadura (dB)";

    /// <summary>Leyenda del mapa de efecto de la armadura: diferencia en dB, no nivel de señal.</summary>
    public static Legend ForReinforcementEffect(RadioSettings settings, EffectScale scale, PropagationModel model = PropagationModel.StraightLine) => new(
        ReinforcementEffectTitle,
        [
            string.Create(Es, $"Banda {BandInfo.Label(settings.Band)} ({settings.FrequencyMHz:0} MHz)"),
            "Diferencia con columnas sin armadura",
            ModelLine(model),
        ],
        scale.Levels());

    /// <summary>Alto de fila proporcional al tamaño del mapa, con un mínimo legible.</summary>
    public static double RowHeightM(double mapExtentM) => Math.Max(mapExtentM / 30.0, 0.2);
}
