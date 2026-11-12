using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class GridTests
{
    [Fact]
    public void Cover_creates_cells_that_contain_the_bounds()
    {
        var spec = GridSpec.Cover(new BoundingBox(new Vec2(0.2, 0.2), new Vec2(10, 5)), 0.5);

        Assert.Equal(20, spec.Columns);
        Assert.Equal(10, spec.Rows);
        Assert.Equal(new Vec2(0.25, 0.25), spec.CellCenter(0, 0));
    }

    [Theory]
    [InlineData(-40, 0)]
    [InlineData(-50, 0)]
    [InlineData(-50.01, 1)]
    [InlineData(-67, 2)]
    [InlineData(-67.01, 3)]
    [InlineData(-85, 4)]
    [InlineData(-120, 5)]
    public void Scale_uses_documented_cut_points(double dbm, int expectedIndex)
    {
        Assert.Equal(expectedIndex, SignalScale.IndexOf(dbm));
    }
}
