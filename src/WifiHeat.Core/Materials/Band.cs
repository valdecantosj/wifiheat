namespace WifiHeat.Core.Materials;

/// <summary>Banda Wi-Fi simulada.</summary>
public enum Band
{
    Ghz2_4,
    Ghz5,
}

public static class BandInfo
{
    /// <summary>
    /// Frecuencia representativa por banda: 2437 MHz (canal 6) y 5180 MHz (canal 36).
    /// No implica selección de canal.
    /// </summary>
    public static double RepresentativeFrequencyMHz(Band band) => band switch
    {
        Band.Ghz2_4 => 2437,
        Band.Ghz5 => 5180,
        _ => throw new ArgumentOutOfRangeException(nameof(band)),
    };

    public static string Label(Band band) => band == Band.Ghz2_4 ? "2,4 GHz" : "5 GHz";
}
