using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Workflow;

public class CoverageAreaTests
{
    private static readonly MaterialCatalog Catalog = new([new Material("test", "Prueba", 5, 12)]);

    private static Wall Box(double width, double height) => new("BOX",
    [
        new Segment2(new Vec2(0, 0), new Vec2(width, 0)),
        new Segment2(new Vec2(width, 0), new Vec2(width, height)),
        new Segment2(new Vec2(width, height), new Vec2(0, height)),
        new Segment2(new Vec2(0, height), new Vec2(0, 0)),
    ], "test", 0.2);

    private static SimulationSession Session(Wall wall, Vec2 ap)
    {
        var session = new SimulationSession(Catalog);
        session.LoadWalls([wall]);
        session.PlaceAccessPoint(ap);
        return session;
    }

    [Fact]
    public void Default_area_is_the_exact_wall_rectangle()
    {
        SimulationSession session = Session(Box(10, 8), new Vec2(2, 4));

        RssiGrid grid = session.Calculate().Value!;

        Assert.Equal(new GridSpec(new Vec2(0, 0), 0.5, 20, 16), grid.Spec);
        Assert.Equal(new BoundingBox(new Vec2(0, 0), new Vec2(10, 8)), grid.Area.Bounds);
        Assert.Equal(grid.Spec.CellCount, grid.ValueCount);
    }

    [Fact]
    public void Access_point_outside_does_not_grow_the_grid()
    {
        SimulationSession session = Session(Box(10, 8), new Vec2(-30, 4));

        RssiGrid grid = session.Calculate().Value!;

        Assert.Equal(320, grid.Spec.CellCount);
        Assert.Equal(new Vec2(0, 0), grid.Spec.Origin);
    }

    [Fact]
    public void Cells_outside_a_custom_area_have_no_value()
    {
        SimulationSession session = Session(Box(10, 8), new Vec2(2, 2));
        session.SetCoverageArea(new Polygon2(
            [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 4), new Vec2(4, 4), new Vec2(4, 8), new Vec2(0, 8)]));

        RssiGrid grid = session.Calculate().Value!;

        Assert.False(grid.HasValue(15, 12));
        Assert.True(double.IsNaN(grid[15, 12]));
        Assert.True(grid.HasValue(2, 12));
        Assert.True(grid.HasValue(15, 2));
        Assert.Equal(20 * 8 + 8 * 8, grid.ValueCount);
        Assert.False(double.IsNaN(grid.Min));
    }

    [Fact]
    public void Resetting_the_area_goes_back_to_the_rectangle()
    {
        SimulationSession session = Session(Box(10, 8), new Vec2(2, 2));
        session.SetCoverageArea(Polygon2.Rectangle(new BoundingBox(new Vec2(0, 0), new Vec2(4, 4))));
        Assert.Equal(64, session.Calculate().Value!.Spec.CellCount);

        session.SetCoverageArea(null);

        Assert.Equal(320, session.Calculate().Value!.Spec.CellCount);
    }

    [Fact]
    public void Aligned_walls_need_an_explicit_area()
    {
        var line = new Wall("W", [new Segment2(new Vec2(5, -5), new Vec2(5, 5))], "test", 0.15);
        SimulationSession session = Session(line, new Vec2(0, 0));

        OperationResult<RssiGrid> result = session.Calculate();
        Assert.False(result.Succeeded);
        Assert.Contains("WIFIAREA", result.Errors[0]);

        session.SetCoverageArea(Polygon2.Rectangle(new BoundingBox(new Vec2(0, -5), new Vec2(10, 5))));
        Assert.True(session.Calculate().Succeeded);
    }

    [Fact]
    public void Area_change_invalidates_the_last_grid()
    {
        SimulationSession session = Session(Box(10, 8), new Vec2(2, 2));
        session.Calculate();

        session.SetCoverageArea(null);

        Assert.Null(session.LastGrid);
    }
}
