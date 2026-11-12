using WifiHeat.Core.Propagation;
using Xunit;

namespace WifiHeat.Core.Tests.Propagation;

public class PathLossTests
{
    [Fact]
    public void Free_space_at_5800MHz_and_0_1km_is_87_71_dB()
    {
        Assert.Equal(87.71, PathLoss.FreeSpaceDb(5800, 0.1), 0.02);
    }

    [Fact]
    public void Free_space_at_2400MHz_and_1m_is_40_05_dB()
    {
        Assert.Equal(40.05, PathLoss.FreeSpaceDbFromMetres(2400, 1), 0.01);
    }

    [Fact]
    public void Metres_and_kilometres_give_the_same_loss()
    {
        Assert.Equal(PathLoss.FreeSpaceDb(2437, 0.025), PathLoss.FreeSpaceDbFromMetres(2437, 25), 9);
    }

    [Fact]
    public void Doubling_distance_adds_20_log10_2()
    {
        double l10 = PathLoss.FreeSpaceDbFromMetres(2437, 10);
        double l20 = PathLoss.FreeSpaceDbFromMetres(2437, 20);
        Assert.Equal(20 * Math.Log10(2), l20 - l10, 9);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.3)]
    [InlineData(1)]
    public void Distances_below_minimum_use_one_metre(double d)
    {
        double loss = PathLoss.FreeSpaceDbFromMetres(2437, d);
        Assert.Equal(PathLoss.FreeSpaceDb(2437, 0.001), loss, 9);
        Assert.True(double.IsFinite(loss));
    }

    [Fact]
    public void Five_GHz_loses_more_than_2_4_GHz_at_same_distance()
    {
        Assert.True(PathLoss.FreeSpaceDbFromMetres(5180, 10) > PathLoss.FreeSpaceDbFromMetres(2437, 10));
    }
}
