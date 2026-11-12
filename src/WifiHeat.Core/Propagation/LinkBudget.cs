namespace WifiHeat.Core.Propagation;

/// <summary>
/// P_RX = P_TX − L_cable,TX + G_antena,TX − L_0 − L_materiales + G_antena,RX − L_cable,RX.
/// Las pérdidas se reciben en dB positivos.
/// </summary>
public static class LinkBudget
{
    public static double ReceivedPowerDbm(RadioSettings settings, double freeSpaceLossDb, double materialsLossDb) =>
        settings.TxPowerDbm
        - settings.TxCableLossDb
        + settings.TxAntennaGainDbi
        - freeSpaceLossDb
        - materialsLossDb
        + settings.RxAntennaGainDbi
        - settings.RxCableLossDb;
}
