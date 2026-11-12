using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Workflow;

public class ScaleValidationTests
{
    private static readonly MaterialCatalog Catalog = new([new Material("test", "Prueba", 5, 12)]);

    private static Wall WallOfLength(double lengthM) =>
        new("A1", [new Segment2(new Vec2(0, 0), new Vec2(lengthM, 0))], "test", 0.15);

    [Theory]
    [InlineData(2)]
    [InlineData(80)]
    [InlineData(500)]
    public void Plausible_extent_has_no_scale_warning(double lengthM)
    {
        ValidationReport report = SceneValidator.Validate([WallOfLength(lengthM)], Catalog);

        Assert.DoesNotContain(report.Issues, i => i.Code == ValidationIssue.SuspiciousScale);
    }

    [Theory]
    [InlineData(0.08)]
    [InlineData(1.99)]
    [InlineData(500.5)]
    [InlineData(82000)]
    public void Implausible_extent_is_a_warning_and_does_not_block(double lengthM)
    {
        ValidationReport report = SceneValidator.Validate([WallOfLength(lengthM)], Catalog);

        ValidationIssue issue = Assert.Single(report.Issues, i => i.Code == ValidationIssue.SuspiciousScale);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.True(report.IsValid);
    }

    [Fact]
    public void No_scale_warning_without_segments()
    {
        Assert.Null(SceneValidator.ScaleIssue([]));
    }

    [Fact]
    public void Missing_unit_blocks_the_calculation()
    {
        var session = new SimulationSession(Catalog);
        ValidationReport report = session.LoadWalls([WallOfLength(10)], [DrawingIssues.MissingUnit()]);
        session.PlaceAccessPoint(new Vec2(1, 1));

        Assert.Contains(report.Issues, i => i.Code == ValidationIssue.Units && i.Severity == IssueSeverity.Error);
        Assert.False(session.Calculate().Succeeded);
    }

    [Fact]
    public void Polyline_with_arcs_is_a_geometry_error_with_entity_reference()
    {
        ValidationIssue issue = DrawingIssues.PolylineWithArcs("2E1");

        Assert.Equal(IssueSeverity.Error, issue.Severity);
        Assert.Equal(ValidationIssue.InvalidGeometry, issue.Code);
        Assert.Equal("2E1", issue.EntityRef);
    }
}
