using WifiHeat.Core.Propagation;
using Xunit;

namespace WifiHeat.Core.Tests.Propagation;

public class LinkBudgetTests
{
    [Fact]
    public void Received_power_with_cables_and_antenna_gains()
    {
        var settings = new RadioSettings
        {
            TxPowerDbm = 20,
            TxCableLossDb = 1,
            TxAntennaGainDbi = 3,
            RxAntennaGainDbi = 1,
            RxCableLossDb = 1,
        };
        Assert.Equal(-65.71, LinkBudget.ReceivedPowerDbm(settings, freeSpaceLossDb: 87.71, materialsLossDb: 0), 2);
    }

    [Fact]
    public void Materials_loss_is_subtracted_once()
    {
        var settings = new RadioSettings { TxPowerDbm = 20, FrequencyMHzOverride = 2400 };
        double l0 = PathLoss.FreeSpaceDbFromMetres(settings.FrequencyMHz, 10);
        Assert.Equal(-40.05, LinkBudget.ReceivedPowerDbm(settings, l0, 0), 2);
        Assert.Equal(-45.05, LinkBudget.ReceivedPowerDbm(settings, l0, 5), 2);
    }
}
