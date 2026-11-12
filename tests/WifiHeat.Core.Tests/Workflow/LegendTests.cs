using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Workflow;

public class LegendTests
{
    [Fact]
    public void Shows_band_frequency_and_tx_power()
    {
        Legend legend = Legend.For(new RadioSettings { Band = Band.Ghz5, TxPowerDbm = 17.5 });

        Assert.Contains(legend.Details, d => d.Contains("5 GHz") && d.Contains("5180 MHz"));
        Assert.Contains(legend.Details, d => d.Contains("17,5 dBm"));
    }

    [Fact]
    public void Uses_the_same_levels_as_the_scale()
    {
        Legend legend = Legend.For(new RadioSettings());

        Assert.Equal(SignalScale.Levels, legend.Levels);
        Assert.Equal(1 + legend.Details.Count + SignalScale.Levels.Count, legend.RowCount);
    }

    [Theory]
    [InlineData(3, 0.2)]
    [InlineData(30, 1.0)]
    [InlineData(90, 3.0)]
    public void Row_height_follows_the_map_extent(double extentM, double expectedM)
    {
        Assert.Equal(expectedM, Legend.RowHeightM(extentM), 9);
    }
}
