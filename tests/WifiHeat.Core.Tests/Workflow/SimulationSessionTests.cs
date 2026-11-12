using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Workflow;

public class SimulationSessionTests
{
    private static readonly MaterialCatalog Catalog = new([new Material("test", "Prueba", 5, 12)]);
    private static readonly Segment2 Axis = new(new Vec2(5, -5), new Vec2(5, 5));

    [Fact]
    public void Missing_material_is_reported_as_FALTA_PROPIEDAD_with_wall_id()
    {
        var session = new SimulationSession(Catalog);
        ValidationReport report = session.LoadWalls([new Wall("1A2B", [Axis], null, 0.15)]);

        Assert.False(report.IsValid);
        ValidationIssue issue = Assert.Single(report.Issues);
        Assert.Equal(ValidationIssue.MissingProperty, issue.Code);
        Assert.Equal("1A2B", issue.EntityRef);
    }

    [Fact]
    public void Missing_thickness_is_reported()
    {
        var session = new SimulationSession(Catalog);
        ValidationReport report = session.LoadWalls([new Wall("W", [Axis], "test", null)]);

        Assert.Contains(report.Issues, i => i.Code == ValidationIssue.MissingProperty && i.Message.Contains("espesor"));
    }

    [Fact]
    public void Unknown_material_is_reported()
    {
        var session = new SimulationSession(Catalog);
        ValidationReport report = session.LoadWalls([new Wall("W", [Axis], "plomo", 0.15)]);

        Assert.Contains(report.Issues, i => i.Code == ValidationIssue.UnknownMaterial);
    }

    [Fact]
    public void Calculate_requires_valid_plan_and_access_point()
    {
        var session = new SimulationSession(Catalog);
        Assert.False(session.Calculate().Succeeded);

        session.LoadWalls([new Wall("W", [Axis], "test", 0.15)]);
        OperationResult<RssiGrid> noAp = session.Calculate();
        Assert.False(noAp.Succeeded);
        Assert.Contains("AP", noAp.Errors[0]);
    }

    [Fact]
    public void Full_flow_produces_grid_and_explained_query()
    {
        var session = new SimulationSession(Catalog);
        session.LoadWalls([new Wall("W", [Axis], "test", 0.15)]);
        session.PlaceAccessPoint(new Vec2(0, 0));
        session.SetCoverageArea(Polygon2.Rectangle(new BoundingBox(new Vec2(-1, -5), new Vec2(11, 5))));

        OperationResult<RssiGrid> grid = session.Calculate();
        Assert.True(grid.Succeeded);
        Assert.True(grid.Value!.Spec.CellCount > 0);

        OperationResult<PointCalculation> q = session.Query(new Vec2(10, 0));
        Assert.True(q.Succeeded);
        string text = Explanation.Format(q.Value!);
        Assert.Contains("Muro W", text);
        Assert.Contains("RSSI estimado", text);
    }

    [Fact]
    public void Host_issues_block_calculation()
    {
        var session = new SimulationSession(Catalog);
        session.LoadWalls([new Wall("W", [Axis], "test", 0.15)],
            [new ValidationIssue(IssueSeverity.Error, ValidationIssue.Units, null, "INSUNITS sin unidad")]);
        session.PlaceAccessPoint(new Vec2(0, 0));

        Assert.False(session.Calculate().Succeeded);
    }

    [Fact]
    public void Walls_far_apart_are_rejected_instead_of_calculated()
    {
        var session = new SimulationSession(Catalog);
        session.LoadWalls([new Wall("W", [Axis, new Segment2(new Vec2(5000, 5000), new Vec2(5001, 5000))], "test", 0.15)]);
        session.PlaceAccessPoint(new Vec2(0, 0));

        OperationResult<RssiGrid> result = session.Calculate();

        Assert.False(result.Succeeded);
        Assert.Contains(ValidationIssue.LargeArea, Assert.Single(result.Errors));
        Assert.Null(session.LastGrid);
    }
}
