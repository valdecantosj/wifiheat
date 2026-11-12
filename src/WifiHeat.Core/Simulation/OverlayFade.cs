using System.Globalization;

namespace WifiHeat.Core.Simulation;

/// <summary>
/// Atenuación del mapa en porcentaje (0 = colores plenos). La imagen se genera opaca dentro del área;
/// en el dibujo se aplica con la propiedad Fade de la imagen y en la exportación mezclando con el fondo blanco.
/// </summary>
public static class OverlayFade
{
    public const int DefaultPercent = 40;
    public const int MinPercent = 0;
    public const int MaxPercent = 90;

    /// <summary>Opacidad equivalente para muestras de la leyenda y la exportación (0–255).</summary>
    public static byte Alpha(int percent) => (byte)Math.Round(255 * (100 - Math.Clamp(percent, MinPercent, MaxPercent)) / 100.0, MidpointRounding.AwayFromZero);

    public static string? Error(int percent) =>
        percent is >= MinPercent and <= MaxPercent
            ? null
            : string.Create(CultureInfo.GetCultureInfo("es-AR"), $"La atenuación debe estar entre {MinPercent} y {MaxPercent} %.");
}
