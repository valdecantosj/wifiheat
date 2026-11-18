using WifiHeat.Core.Propagation;

namespace WifiHeat.Core.Materials;

/// <summary>
/// Material de muro con su pérdida de penetración por banda (dB, valor positivo).
/// Reflection: propiedades eléctricas para calcular el rebote; null si el material no refleja en el modelo.
/// </summary>
public sealed record Material(string Id, string Name, double LossDb2_4, double LossDb5, Dielectric? Reflection = null)
{
    public double LossDb(Band band) => band == Band.Ghz2_4 ? LossDb2_4 : LossDb5;
}
