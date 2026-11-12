namespace WifiHeat.Core.Materials;

/// <summary>Material de muro con su pérdida de penetración por banda (dB, valor positivo).</summary>
public sealed record Material(string Id, string Name, double LossDb2_4, double LossDb5)
{
    public double LossDb(Band band) => band == Band.Ghz2_4 ? LossDb2_4 : LossDb5;
}
