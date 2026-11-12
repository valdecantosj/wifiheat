using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Workflow;

public class ResolutionTests
{
    private static readonly MaterialCatalog Catalog = new([new Material("test", "Prueba", 5, 12)]);

    private static SimulationSession Room(double sizeM)
    {
        var session = new SimulationSession(Catalog);
        Vec2[] c = [new(0, 0), new(sizeM, 0), new(sizeM, sizeM), new(0, sizeM)];
        session.LoadWalls([new Wall("R", [new(c[0], c[1]), new(c[1], c[2]), new(c[2], c[3]), new(c[3], c[0])], "test", 0.15)]);
        session.PlaceAccessPoint(new Vec2(1, 1));
        return session;
    }

    [Fact]
    public void Default_resolution_is_half_a_metre()
    {
        Assert.Equal(0.5, new SimulationSession(Catalog).CellSizeM);
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(0.25)]
    [InlineData(5)]
    public void Accepted_resolution_is_used_by_the_next_calculation(double cellSizeM)
    {
        SimulationSession session = Room(10);

        Assert.True(session.SetResolution(cellSizeM).Succeeded);
        RssiGrid grid = session.Calculate().Value!;

        Assert.Equal(cellSizeM, grid.Spec.CellSizeM);
        Assert.Equal((int)Math.Ceiling(10 / cellSizeM - 1e-9), grid.Spec.Columns);
    }

    [Theory]
    [InlineData(0.09)]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(5.01)]
    [InlineData(double.NaN)]
    public void Out_of_range_resolution_is_rejected_and_the_previous_one_kept(double cellSizeM)
    {
        SimulationSession session = Room(10);

        OperationResult<double> result = session.SetResolution(cellSizeM);

        Assert.False(result.Succeeded);
        Assert.Contains("0,10", Assert.Single(result.Errors));
        Assert.Equal(SimulationSession.DefaultCellSizeM, session.CellSizeM);
    }

    [Fact]
    public void Changing_the_resolution_discards_the_last_grid()
    {
        SimulationSession session = Room(10);
        session.Calculate();

        session.SetResolution(0.25);

        Assert.Null(session.LastGrid);
    }

    [Fact]
    public void Fine_resolution_on_a_large_area_hits_the_cell_cap()
    {
        SimulationSession session = Room(200);
        session.SetResolution(0.1);

        OperationResult<RssiGrid> result = session.Calculate();

        Assert.False(result.Succeeded);
        Assert.Contains(ValidationIssue.LargeArea, Assert.Single(result.Errors));
    }
}
