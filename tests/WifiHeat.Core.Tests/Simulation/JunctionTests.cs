using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class JunctionTests
{
    private static readonly MaterialCatalog Catalog = new(
    [
        new Material("light", "Liviano", 3, 4),
        new Material("heavy", "Pesado", 8, 12),
    ]);

    private static readonly RadioSettings Settings = new() { TxPowerDbm = 20, FrequencyMHzOverride = 2400 };

    private static Wall WallOf(string id, string material, params Segment2[] segments) => new(id, segments, material, 0.15);

    private static Segment2 S(double ax, double ay, double bx, double by) => new(new Vec2(ax, ay), new Vec2(bx, by));

    private static PointCalculation Calc(SimulationScene scene, Vec2 ap, Vec2 point) =>
        PointCalculator.Calculate(scene, new AccessPoint(ap), Settings, point);

    [Fact]
    public void T_junction_counts_the_largest_loss_once()
    {
        var scene = new SimulationScene(
        [
            WallOf("A", "light", S(0, 7, 20, 7)),
            WallOf("B", "heavy", S(10, 0, 10, 7)),
        ], Catalog);

        PointCalculation c = Calc(scene, new Vec2(8, 3), new Vec2(12, 11));

        WallCrossing w = Assert.Single(c.Walls);
        Assert.Equal("B", w.WallId);
        Assert.Equal(["A"], w.JunctionWith);
        Assert.Equal(8, c.WallLossDb, 9);
    }

    [Fact]
    public void Cross_junction_counts_the_largest_loss_once()
    {
        var scene = new SimulationScene(
        [
            WallOf("H", "heavy", S(0, 5, 10, 5)),
            WallOf("V", "light", S(5, 0, 5, 10)),
        ], Catalog);

        PointCalculation c = Calc(scene, new Vec2(1, 1), new Vec2(9, 9));

        WallCrossing w = Assert.Single(c.Walls);
        Assert.Equal("H", w.WallId);
        Assert.Equal(8, c.WallLossDb, 9);
    }

    [Fact]
    public void Path_exactly_through_the_corner_of_two_walls_counts_once()
    {
        var scene = new SimulationScene(
        [
            WallOf("A", "light", S(5, 5, 10, 5)),
            WallOf("B", "light", S(5, 5, 5, 10)),
        ], Catalog);

        PointCalculation c = Calc(scene, new Vec2(0, 0), new Vec2(10, 10));

        Assert.Single(c.Walls);
        Assert.Equal(3, c.WallLossDb, 9);
    }

    [Fact]
    public void Path_through_a_vertex_of_one_polyline_counts_that_wall_once()
    {
        var scene = new SimulationScene([WallOf("L", "heavy", S(0, 5, 5, 5), S(5, 5, 5, 10))], Catalog);

        PointCalculation c = Calc(scene, new Vec2(0, 0), new Vec2(10, 10));

        WallCrossing w = Assert.Single(c.Walls);
        Assert.Empty(w.JunctionWith);
        Assert.Equal(8, c.WallLossDb, 9);
    }

    [Fact]
    public void Walls_touched_less_than_a_millimetre_apart_count_once()
    {
        var scene = new SimulationScene(
        [
            WallOf("A", "light", S(5, -5, 5, 5)),
            WallOf("B", "heavy", S(5.0005, -5, 5.0005, 5)),
        ], Catalog);

        PointCalculation c = Calc(scene, new Vec2(0, 0), new Vec2(10, 0));

        Assert.Single(c.Walls);
        Assert.Equal(8, c.WallLossDb, 9);
    }

    [Fact]
    public void Walls_touched_at_different_points_add_up()
    {
        var scene = new SimulationScene(
        [
            WallOf("A", "light", S(5, -5, 5, 5)),
            WallOf("B", "heavy", S(5.005, -5, 5.005, 5)),
        ], Catalog);

        PointCalculation c = Calc(scene, new Vec2(0, 0), new Vec2(10, 0));

        Assert.Equal(2, c.Walls.Count);
        Assert.Equal(11, c.WallLossDb, 9);
    }

    [Fact]
    public void Path_along_a_wall_groups_the_walls_it_meets_there()
    {
        var scene = new SimulationScene(
        [
            WallOf("A", "light", S(2, 0, 8, 0)),
            WallOf("B", "heavy", S(5, -3, 5, 0)),
            WallOf("C", "light", S(9, -3, 9, 3)),
        ], Catalog);

        PointCalculation c = Calc(scene, new Vec2(0, 0), new Vec2(10, 0));

        Assert.Equal(2, c.Walls.Count);
        Assert.Equal(11, c.WallLossDb, 9);
    }

    [Theory]
    [InlineData(12, 11)]
    [InlineData(10, 10)]
    [InlineData(9.3, 4.1)]
    public void Fast_path_matches_explained_path_at_junctions(double x, double y)
    {
        var scene = new SimulationScene(
        [
            WallOf("A", "light", S(0, 7, 20, 7)),
            WallOf("B", "heavy", S(10, 0, 10, 7)),
            WallOf("C", "light", S(5, 5, 10, 5)),
        ], Catalog);
        var ap = new AccessPoint(new Vec2(8, 3));

        Assert.Equal(PointCalculator.Calculate(scene, ap, Settings, new Vec2(x, y)).ReceivedPowerDbm,
                     PointCalculator.ReceivedPowerDbm(scene, ap, Settings, new Vec2(x, y)), 9);
    }
}
