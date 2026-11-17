using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Walls;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Service;

public class ColumnServiceTests
{
    private static readonly BoundingBox Window = new(new Vec2(-1, -1), new Vec2(11, 9));

    private static FakeDrawing WithHatchAndOutline()
    {
        var d = new FakeDrawing(FakeDrawing.CatalogWithConcrete);
        d.AddFigure("H1", "HATCH", 5, 3, 0.3, 0.3);
        d.AddFigure("P1", "LWPOLYLINE", 5.01, 3.01, 0.29, 0.29);
        return d;
    }

    private static Polygon2 Square(double x, double y, double side) =>
        Polygon2.Rectangle(new BoundingBox(new Vec2(x, y), new Vec2(x + side, y + side)));

    [Fact]
    public void A_hatch_and_its_outline_make_a_single_column()
    {
        FakeDrawing d = WithHatchAndOutline();

        OperationResult<ColumnGeneration> result = d.AssignColumns(["H1", "P1"], Reinforcement.Medium);

        Assert.True(result.Succeeded);
        ColumnGeneration g = result.Value!;
        Assert.Equal((1, 0, 1), (g.Created, g.Kept, g.Duplicates));
        Assert.Equal(5, g.AdditionalLossDb);
        Wall column = d.Walls[Assert.Single(g.ColumnHandles)];
        Assert.True(column.IsColumn);
        Assert.Equal("concrete", column.MaterialId);
        Assert.Equal(Reinforcement.Medium, column.Reinforcement);
        Assert.Equal(4, column.Segments.Count);
        Assert.Equal(2, d.Figures.Count);
        Assert.True(d.Validate().IsValid);
    }

    [Fact]
    public void Repeating_the_action_does_not_duplicate_and_updates_the_reinforcement()
    {
        FakeDrawing d = WithHatchAndOutline();
        d.AssignColumns(["H1", "P1"], Reinforcement.Low);

        OperationResult<ColumnGeneration> again = d.AssignColumns(["P1", "H1"], Reinforcement.High);

        Assert.Equal((0, 1, 1), (again.Value!.Created, again.Value.Kept, again.Value.Duplicates));
        Assert.Equal(Reinforcement.High, Assert.Single(d.Walls.Values).Reinforcement);
    }

    [Fact]
    public void An_existing_column_can_be_chosen_to_change_its_reinforcement()
    {
        FakeDrawing d = WithHatchAndOutline();
        string handle = d.AssignColumns(["H1"], Reinforcement.Low).Value!.ColumnHandles[0];

        OperationResult<ColumnGeneration> result = d.AssignColumns([handle], Reinforcement.Medium);

        Assert.Equal((0, 1), (result.Value!.Created, result.Value.Kept));
        Assert.Equal(Reinforcement.Medium, d.Walls[handle].Reinforcement);
    }

    [Fact]
    public void A_figure_larger_than_a_column_is_not_generated()
    {
        FakeDrawing d = WithHatchAndOutline();
        d.AddFigure("ROOM", "LWPOLYLINE", 0, 0, 1.6, 1.2);

        OperationResult<ColumnGeneration> mixed = d.AssignColumns(["H1", "ROOM"], Reinforcement.Medium);
        OperationResult<ColumnGeneration> alone = d.AssignColumns(["ROOM"], Reinforcement.Medium);

        Assert.True(mixed.Succeeded);
        Assert.Equal(["ROOM"], mixed.Value!.TooLarge);
        Assert.Contains("No parece una columna", Assert.Single(mixed.Warnings));
        Assert.False(alone.Succeeded);
        Assert.Contains(alone.Errors, e => e.Contains("No parece una columna") && e.Contains("ROOM"));
        Assert.Single(d.Walls);
    }

    [Fact]
    public void A_hatch_with_islands_or_arcs_is_a_geometry_error_with_its_handle()
    {
        var d = new FakeDrawing(FakeDrawing.CatalogWithConcrete);
        d.AddBrokenFigure("H9", "HATCH", "tiene islas o más de un contorno");
        d.AddLine("0", "L1", 0, 0, 1, 0);

        OperationResult<ColumnGeneration> result = d.AssignColumns(["H9", "L1", "NOPE"], Reinforcement.Medium);

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.StartsWith(ValidationIssue.InvalidGeometry) && e.Contains("H9") && e.Contains("islas") && e.Contains("L1"));
        Assert.Contains(result.Errors, e => e.Contains("NOPE"));
        Assert.Empty(d.Walls);
    }

    [Fact]
    public void Columns_need_the_unit_a_known_level_and_concrete_in_the_catalog()
    {
        var noConcrete = new FakeDrawing();
        noConcrete.AddFigure("H1", "HATCH", 5, 3, 0.3, 0.3);
        FakeDrawing noUnit = WithHatchAndOutline();
        noUnit.Unit = null;

        Assert.Contains("concrete", Assert.Single(noConcrete.AssignColumns(["H1"], Reinforcement.Medium).Errors));
        Assert.Contains("WIFISETUP", Assert.Single(noUnit.AssignColumns(["H1"], Reinforcement.Medium).Errors));
        Assert.False(WithHatchAndOutline().AssignColumns(["H1"], (Reinforcement)9).Succeeded);
        Assert.False(WithHatchAndOutline().AssignColumns([], Reinforcement.Medium).Succeeded);
    }

    [Fact]
    public void The_wall_list_tells_columns_from_walls()
    {
        FakeDrawing d = WithHatchAndOutline();
        d.Add("A1", 0, 0, 10, 0, "brick", 0.2);
        d.AssignColumns(["H1"], Reinforcement.High);

        IReadOnlyList<WallInfo> walls = d.LoadWalls();

        WallInfo column = Assert.Single(walls, w => w.IsColumn);
        Assert.Equal(Reinforcement.High, column.Reinforcement);
        Assert.Equal(1.2, column.LengthM, 9);
        Assert.Null(column.ThicknessM);
        Assert.False(walls.Single(w => w.Handle == "A1").IsColumn);
    }

    [Fact]
    public void The_value_behind_the_column_drops_by_concrete_plus_reinforcement()
    {
        FakeDrawing d = WithHatchAndOutline();
        d.Add("A1", 0, 0, 10, 0, "brick", 0.2);
        d.Add("A2", 0, 8, 10, 8, "brick", 0.2);
        d.SetAccessPoint(new Vec2(1, 3.15));
        double before = d.Query(new Vec2(9, 3.15)).Value!.ReceivedPowerDbm;

        d.AssignColumns(["H1", "P1"], Reinforcement.Medium);
        QueryResult after = d.Query(new Vec2(9, 3.15)).Value!;

        Assert.Equal(before - 25, after.ReceivedPowerDbm, 9);
        Assert.Single(after.Calculation.Walls);
        Assert.False(d.Query(new Vec2(5.15, 3.15)).Succeeded);
    }

    [Fact]
    public void Generating_axes_reports_small_closed_figures_once_per_outline()
    {
        FakeDrawing d = WithHatchAndOutline();
        d.AddFigure("BIG", "LWPOLYLINE", 0, 0, 4, 3);
        d.AddFace("muros", "a", 0, 0, 5, 0);
        d.AddFace("muros", "b", 0, 0.2, 5, 0.2);

        AxisGeneration first = d.GenerateAxes(Window, ["muros"]).Value!;
        d.AssignColumns(first.SmallClosedFigures, Reinforcement.Medium);
        AxisGeneration second = d.GenerateAxes(Window, ["muros"]).Value!;

        Assert.Equal(["H1"], first.SmallClosedFigures);
        Assert.Empty(second.SmallClosedFigures);
        Assert.Single(d.Walls.Values, w => w.IsColumn);
    }

    [Fact]
    public void A_small_closed_outline_is_not_taken_as_the_two_faces_of_a_wall()
    {
        var d = new FakeDrawing(FakeDrawing.CatalogWithConcrete);
        d.AddClosedPolyline("0", "COL", 5, 3, 0.3, 0.3);
        d.AddClosedPolyline("0", "THIN", 0, 0, 4, 0.2);

        Assert.False(d.FindFacePairs(["COL"]).HasPairs);
        Assert.True(d.FindFacePairs(["THIN"]).HasPairs);
    }

    [Fact]
    public void Column_rules_use_the_side_and_tolerate_two_centimetres()
    {
        var rotated = new Polygon2([new Vec2(0, 0), new Vec2(0.85, 0.85), new Vec2(0, 1.7), new Vec2(-0.85, 0.85)]);

        Assert.True(ColumnRules.IsSmall(Square(0, 0, 0.3)));
        Assert.True(ColumnRules.IsSmall(Square(0, 0, 1.5)));
        Assert.True(ColumnRules.IsSmall(rotated));
        Assert.False(ColumnRules.IsSmall(Square(0, 0, 1.6)));
        Assert.False(ColumnRules.IsSmall(Polygon2.Rectangle(new BoundingBox(new Vec2(0, 0), new Vec2(0.3, 3)))));
        Assert.True(ColumnRules.SameFootprint(Square(5, 3, 0.3), Square(5.01, 3.01, 0.29)));
        Assert.False(ColumnRules.SameFootprint(Square(5, 3, 0.3), Square(5.05, 3, 0.3)));
        Assert.False(ColumnRules.SameFootprint(Square(5, 3, 0.3), Square(6, 3, 0.3)));
    }
}
