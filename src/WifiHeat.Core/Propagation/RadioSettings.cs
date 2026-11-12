using WifiHeat.Core.Materials;

namespace WifiHeat.Core.Propagation;

/// <summary>Parámetros del enlace. Pérdidas en dB positivos, ganancias en dBi, potencia en dBm.</summary>
public sealed record RadioSettings
{
    public Band Band { get; init; } = Band.Ghz2_4;
    public double TxPowerDbm { get; init; } = 20;
    public double TxCableLossDb { get; init; }
    public double TxAntennaGainDbi { get; init; }
    public double RxAntennaGainDbi { get; init; }
    public double RxCableLossDb { get; init; }

    /// <summary>Si es null se usa la frecuencia representativa de la banda.</summary>
    public double? FrequencyMHzOverride { get; init; }

    public double FrequencyMHz => FrequencyMHzOverride ?? BandInfo.RepresentativeFrequencyMHz(Band);

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (FrequencyMHz <= 0) errors.Add("La frecuencia debe ser mayor que 0 MHz.");
        if (TxCableLossDb < 0) errors.Add("L_cable,TX se expresa como valor positivo (dB).");
        if (RxCableLossDb < 0) errors.Add("L_cable,RX se expresa como valor positivo (dB).");
        if (TxPowerDbm is < -10 or > 40) errors.Add("P_TX debe estar entre -10 y 40 dBm.");
        return errors;
    }
}
