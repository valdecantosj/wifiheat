using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class PointCalculatorTests
{
    private static readonly MaterialCatalog Catalog = new([new Material("test", "Material de prueba", 5, 12)]);

    private static Wall WallOf(string id, params Segment2[] segments) => new(id, segments, "test", 0.15);

    private static readonly RadioSettings Settings24 = new() { TxPowerDbm = 20, FrequencyMHzOverride = 2400 };

    [Fact]
    public void One_wall_subtracts_its_loss_once()
    {
        var scene = new SimulationScene([WallOf("M1", new Segment2(new Vec2(5, -5), new Vec2(5, 5)))], Catalog);
        PointCalculation c = PointCalculator.Calculate(scene, new AccessPoint(new Vec2(0, 0)), Settings24, new Vec2(10, 0));

        Assert.Single(c.Walls);
        Assert.Equal(5, c.WallLossDb, 9);
        Assert.Equal(-45.05, c.ReceivedPowerDbm, 2);
    }

    [Fact]
    public void Shared_vertex_of_same_wall_is_counted_once()
    {
        var wall = WallOf("L", new Segment2(new Vec2(5, -5), new Vec2(5, 0)), new Segment2(new Vec2(5, 0), new Vec2(9, 0)));
        var scene = new SimulationScene([wall], Catalog);
        PointCalculation c = PointCalculator.Calculate(scene, new AccessPoint(new Vec2(0, -5)), Settings24, new Vec2(10, 5));

        Assert.Single(c.Walls);
    }

    [Fact]
    public void Two_distinct_walls_add_up()
    {
        var scene = new SimulationScene(
        [
            WallOf("A", new Segment2(new Vec2(3, -5), new Vec2(3, 5))),
            WallOf("B", new Segment2(new Vec2(6, -5), new Vec2(6, 5))),
        ], Catalog);
        PointCalculation c = PointCalculator.Calculate(scene, new AccessPoint(new Vec2(0, 0)), Settings24, new Vec2(10, 0));

        Assert.Equal(2, c.Walls.Count);
        Assert.Equal(10, c.WallLossDb, 9);
    }

    [Fact]
    public void Band_selects_material_loss()
    {
        var scene = new SimulationScene([WallOf("M1", new Segment2(new Vec2(5, -5), new Vec2(5, 5)))], Catalog);
        PointCalculation c = PointCalculator.Calculate(scene, new AccessPoint(new Vec2(0, 0)), new RadioSettings { Band = Band.Ghz5 }, new Vec2(10, 0));

        Assert.Equal(12, c.WallLossDb, 9);
        Assert.Equal(5180, c.Settings.FrequencyMHz);
    }

    [Fact]
    public void Fast_path_matches_explained_path()
    {
        var scene = new SimulationScene([WallOf("M1", new Segment2(new Vec2(5, -5), new Vec2(5, 5)))], Catalog);
        var ap = new AccessPoint(new Vec2(0.3, 0.2));
        var p = new Vec2(8.7, -1.4);

        Assert.Equal(PointCalculator.Calculate(scene, ap, Settings24, p).ReceivedPowerDbm,
                     PointCalculator.ReceivedPowerDbm(scene, ap, Settings24, p), 9);
    }
}
