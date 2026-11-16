using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class ColumnTests
{
    private static readonly MaterialCatalog Catalog = new(
    [
        new Material("concrete", "Hormigón", 22.8, 44.8),
        new Material("brick", "Ladrillo", 9, 17.5),
        new Material("lead", "Plomo", 60, 80),
    ]);

    private static readonly RadioSettings Settings = new() { TxPowerDbm = 20, FrequencyMHzOverride = 2400 };

    private static Segment2 S(double ax, double ay, double bx, double by) => new(new Vec2(ax, ay), new Vec2(bx, by));

    private static Wall Column(string id, double x, double y, double side, Reinforcement? level = Reinforcement.Medium) =>
        Wall.Column(id, Polygon2.Rectangle(new BoundingBox(new Vec2(x, y), new Vec2(x + side, y + side))), "concrete", level);

    private static Wall WallOf(string id, string material, params Segment2[] segments) => new(id, segments, material, 0.15);

    private static PointCalculation Calc(IReadOnlyList<Wall> walls, Vec2 ap, Vec2 point, RadioSettings? settings = null) =>
        PointCalculator.Calculate(new SimulationScene(walls, Catalog), new AccessPoint(ap), settings ?? Settings, point);

    [Theory]
    [InlineData(Reinforcement.Low, 25.3)]
    [InlineData(Reinforcement.Medium, 27.8)]
    [InlineData(Reinforcement.High, 30.3)]
    public void Column_loss_is_concrete_plus_the_reinforcement_additional(Reinforcement level, double expectedDb)
    {
        PointCalculation c = Calc([Column("C", 5, 0, 0.3, level)], new Vec2(0, 0.15), new Vec2(10, 0.15));

        WallCrossing crossing = Assert.Single(c.Walls);
        Assert.Equal(expectedDb, crossing.LossDb, 9);
        Assert.Equal(expectedDb - 22.8, crossing.ReinforcementLossDb, 9);
        Assert.Equal(level, crossing.Reinforcement);
        Assert.True(crossing.IsColumn);
    }

    [Fact]
    public void The_additional_is_the_same_in_both_bands()
    {
        PointCalculation c = Calc([Column("C", 5, 0, 0.3)], new Vec2(0, 0.15), new Vec2(10, 0.15), Settings with { Band = Band.Ghz5, FrequencyMHzOverride = null });

        Assert.Equal(49.8, c.WallLossDb, 9);
    }

    [Fact]
    public void A_ray_through_two_sides_counts_the_column_once()
    {
        PointCalculation c = Calc([Column("C", 5, 0, 0.3)], new Vec2(0, 0.15), new Vec2(10, 0.15));

        Assert.Single(c.Walls);
        Assert.Equal(27.8, c.WallLossDb, 9);
        Assert.Equal(27.8, PathCrossings.TotalLossDb(new SimulationScene([Column("C", 5, 0, 0.3)], Catalog), S(0, 0.15, 10, 0.15), Band.Ghz2_4), 9);
    }

    [Fact]
    public void A_ray_through_a_diagonal_counts_the_column_once()
    {
        PointCalculation c = Calc([Column("C", 5, 0, 0.3)], new Vec2(4, 1.3), new Vec2(6, -0.7));

        Assert.Single(c.Walls);
        Assert.Equal(27.8, c.WallLossDb, 9);
    }

    [Fact]
    public void A_ray_that_grazes_a_corner_counts_the_column_once()
    {
        PointCalculation c = Calc([Column("C", 5, 0, 0.3)], new Vec2(4, -0.7), new Vec2(6, 1.3));

        Assert.Single(c.Walls);
        Assert.Equal(27.8, c.WallLossDb, 9);
    }

    [Fact]
    public void A_ray_that_misses_the_column_has_no_loss()
    {
        PointCalculation c = Calc([Column("C", 5, 0, 0.3)], new Vec2(0, 1), new Vec2(10, 1));

        Assert.Empty(c.Walls);
    }

    [Fact]
    public void A_column_touching_a_wall_at_the_crossing_counts_once_with_the_largest_loss()
    {
        PointCalculation c = Calc(
            [Column("C", 5, 0, 0.3), WallOf("W", "brick", S(5.3, -3, 5.3, 3))],
            new Vec2(0, 0.15), new Vec2(10, 0.15));

        WallCrossing crossing = Assert.Single(c.Walls);
        Assert.Equal("C", crossing.WallId);
        Assert.Equal(["W"], crossing.JunctionWith);
        Assert.Equal(27.8, c.WallLossDb, 9);
    }

    [Fact]
    public void A_wall_with_more_loss_than_the_column_it_touches_is_the_one_counted()
    {
        PointCalculation c = Calc(
            [Column("C", 5, 0, 0.3), WallOf("W", "lead", S(5.3, -3, 5.3, 3))],
            new Vec2(0, 0.15), new Vec2(10, 0.15));

        WallCrossing crossing = Assert.Single(c.Walls);
        Assert.Equal("W", crossing.WallId);
        Assert.False(crossing.IsColumn);
        Assert.Equal(["C"], crossing.JunctionWith);
        Assert.Equal(60, c.WallLossDb, 9);
    }

    [Fact]
    public void A_column_and_a_separate_wall_add_up()
    {
        PointCalculation c = Calc(
            [Column("C", 5, 0, 0.3), WallOf("W", "brick", S(7, -3, 7, 3))],
            new Vec2(0, 0.15), new Vec2(10, 0.15));

        Assert.Equal(2, c.Walls.Count);
        Assert.Equal(36.8, c.WallLossDb, 9);
    }

    [Fact]
    public void Without_reinforcement_the_column_counts_as_plain_concrete()
    {
        var scene = new SimulationScene([Column("C", 5, 0, 0.3, Reinforcement.High)], Catalog, includeReinforcement: false);

        Assert.Equal(22.8, PathCrossings.TotalLossDb(scene, S(0, 0.15, 10, 0.15), Band.Ghz2_4), 9);
    }

    [Fact]
    public void The_explanation_names_the_column_and_splits_its_loss()
    {
        string text = Explanation.Format(Calc([Column("C1", 5, 0, 0.3)], new Vec2(0, 0.15), new Vec2(10, 0.15)));

        Assert.Contains("Muros y columnas cruzados: 1", text);
        Assert.Contains("Columna C1 · Hormigón con armadura media: 27,8 dB (22,8 + 5,0 de armadura)", text);
    }

    [Fact]
    public void An_access_point_inside_a_column_is_reported_instead_of_calculating()
    {
        var session = new SimulationSession(Catalog);
        session.LoadWalls([Column("C", 5, 0, 0.3), WallOf("A", "brick", S(0, -4, 10, -4)), WallOf("B", "brick", S(0, 4, 10, 4))]);
        session.PlaceAccessPoint(new Vec2(5.15, 0.15));

        OperationResult<RssiGrid> grid = session.Calculate();
        OperationResult<PointCalculation> query = session.Query(new Vec2(8, 0));

        Assert.False(grid.Succeeded);
        Assert.Contains("El AP está dentro de la columna C", Assert.Single(grid.Errors));
        Assert.False(query.Succeeded);
        Assert.Null(session.LastGrid);
    }

    [Fact]
    public void A_query_point_inside_a_column_is_reported_instead_of_calculating()
    {
        var session = new SimulationSession(Catalog);
        session.LoadWalls([Column("C", 5, 0, 0.3), WallOf("A", "brick", S(0, -4, 10, -4)), WallOf("B", "brick", S(0, 4, 10, 4))]);
        session.PlaceAccessPoint(new Vec2(1, 0));

        OperationResult<PointCalculation> inside = session.Query(new Vec2(5.15, 0.15));
        OperationResult<PointCalculation> onEdge = session.Query(new Vec2(5, 0.15));
        OperationResult<PointCalculation> outside = session.Query(new Vec2(8, 0.15));

        Assert.Contains("dentro de la columna C", Assert.Single(inside.Errors));
        Assert.False(onEdge.Succeeded);
        Assert.True(outside.Succeeded);
        Assert.True(session.Calculate().Succeeded);
    }

    [Fact]
    public void A_column_needs_material_and_reinforcement_but_no_thickness()
    {
        ValidationReport complete = SceneValidator.Validate([Column("C", 5, 0, 0.3)], Catalog);
        ValidationReport missing = SceneValidator.Validate([Column("C", 5, 0, 0.3, level: null) with { MaterialId = null }], Catalog);

        Assert.True(complete.IsValid);
        Assert.DoesNotContain(complete.Issues, i => i.Severity == IssueSeverity.Error);
        Assert.Equal(2, missing.Issues.Count(i => i.Code == ValidationIssue.MissingProperty));
    }

    [Fact]
    public void A_column_that_is_not_closed_is_invalid_geometry()
    {
        var open = new Wall("C", [S(5, 0, 5.3, 0), S(5.3, 0, 5.3, 0.3), S(5.3, 0.3, 5, 0.3)], "concrete", null, ObstacleKind.Column, Reinforcement.Low);

        ValidationReport report = SceneValidator.Validate([open], Catalog);

        Assert.Contains(report.Issues, i => i.Code == ValidationIssue.InvalidGeometry && i.Severity == IssueSeverity.Error);
        Assert.Null(open.Footprint());
    }

    [Fact]
    public void The_sides_of_a_column_are_not_reported_as_double_counting()
    {
        ValidationReport report = SceneValidator.Validate([Column("C", 5, 0, 0.3), WallOf("W", "brick", S(5.5, -3, 5.5, 3))], Catalog);

        Assert.DoesNotContain(report.Issues, i => i.Code == ValidationIssue.DoubleCount);
    }
}
