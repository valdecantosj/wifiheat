using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Service;

public class DoubleCountTests
{
    private static FakeDrawing TwoFaces()
    {
        var d = new FakeDrawing();
        d.AddLine("muros", "F1", 5, 0, 5, 8);
        d.AddLine("muros", "F2", 5.2, 0, 5.2, 8);
        return d;
    }

    private static QueryResult QueryAcross(FakeDrawing d)
    {
        d.SetAccessPoint(new Vec2(2, 4));
        OperationResult<QueryResult> query = d.Query(new Vec2(8, 4));
        Assert.True(query.Succeeded);
        return query.Value!;
    }

    [Fact]
    public void FindFacePairs_reports_the_two_faces_of_a_wall()
    {
        FakeDrawing d = TwoFaces();
        d.AddLine("muros", "L", 0, 0, 0, 8);

        FacePairSurvey pairs = d.FindFacePairs(["F1", "F2", "L"]);

        Assert.True(pairs.HasPairs);
        Assert.Equal(1, pairs.PairCount);
        Assert.Equal(["F1", "F2"], pairs.FaceHandles);
        Assert.Equal(0.2, pairs.MinThicknessM, 9);
        Assert.Equal(2, d.Lines.Count(l => l.Value.Layer == "muros" && l.Key != "L"));
        Assert.Empty(d.Walls);
    }

    [Fact]
    public void FindFacePairs_ignores_entities_that_are_already_walls()
    {
        var d = new FakeDrawing();
        d.Add("W1", 5, 0, 5, 8, "brick", 0.15);
        d.Add("W2", 5.2, 0, 5.2, 8, "brick", 0.15);

        Assert.False(d.FindFacePairs(["W1", "W2"]).HasPairs);
    }

    [Fact]
    public void FindFacePairs_without_unit_or_selection_finds_nothing()
    {
        FakeDrawing d = TwoFaces();

        Assert.False(d.FindFacePairs([]).HasPairs);
        d.Unit = null;
        Assert.False(d.FindFacePairs(["F1", "F2"]).HasPairs);
    }

    [Fact]
    public void Joining_faces_creates_one_axis_with_the_material_and_the_measured_thickness()
    {
        FakeDrawing d = TwoFaces();
        d.AddLine("0", "L", 0, 0, 0, 8);

        OperationResult<FaceJoin> result = d.AssignMaterialJoiningFaces(["F1", "F2", "L"], "brick", 0.3);

        Assert.True(result.Succeeded);
        FaceJoin join = result.Value!;
        Assert.Equal((1, 0, 2, 1), (join.AxesCreated, join.AxesKept, join.FaceCount, join.SingleWalls));
        Assert.Equal(2, join.WallCount);

        Wall axis = d.Walls[Assert.Single(join.AxisHandles)];
        Assert.Equal("brick", axis.MaterialId);
        Assert.Equal(0.2, axis.ThicknessM!.Value, 9);
        Assert.Equal(5.1, axis.Segments[0].A.X, 9);
        Assert.Equal("muros", d.Lines["F1"].Layer);
        Assert.Equal("muros", d.Lines["F2"].Layer);
        Assert.Equal(0.3, d.Walls["L"].ThicknessM);
        Assert.Equal(2, d.Walls.Count);
    }

    [Fact]
    public void Joining_faces_counts_the_wall_once()
    {
        FakeDrawing d = TwoFaces();

        d.AssignMaterialJoiningFaces(["F1", "F2"], "brick", null);
        QueryResult query = QueryAcross(d);

        Assert.Single(query.Calculation.Walls);
        Assert.Equal(8, query.Calculation.WallLossDb, 9);
        Assert.DoesNotContain(d.Validate().Items, i => i.Code == ValidationIssue.DoubleCount);
    }

    [Fact]
    public void Joining_the_same_faces_again_keeps_the_axis_and_updates_its_material()
    {
        FakeDrawing d = TwoFaces();
        d.AssignMaterialJoiningFaces(["F1", "F2"], "brick", null);

        OperationResult<FaceJoin> again = d.AssignMaterialJoiningFaces(["F1", "F2"], "gypsum", null);

        Assert.Equal((0, 1), (again.Value!.AxesCreated, again.Value.AxesKept));
        Assert.Equal("gypsum", Assert.Single(d.Walls.Values).MaterialId);
    }

    [Fact]
    public void Joining_without_pairs_assigns_every_entity_like_before()
    {
        var d = new FakeDrawing();
        d.AddLine("0", "L1", 0, 0, 10, 0);
        d.AddLine("0", "L2", 0, 8, 10, 8);

        OperationResult<FaceJoin> result = d.AssignMaterialJoiningFaces(["L1", "L2", "NOPE"], "brick", null);

        Assert.True(result.Succeeded);
        Assert.Equal((0, 2), (result.Value!.AxisCount, result.Value.SingleWalls));
        Assert.Equal(WifiHeatService.DefaultThicknessM, d.Walls["L1"].ThicknessM);
        Assert.Contains("NOPE", Assert.Single(result.Warnings));
    }

    [Fact]
    public void Joining_reports_faces_with_long_unpaired_stretches()
    {
        var d = new FakeDrawing();
        d.AddLine("muros", "LONG", 0, 0, 10, 0);
        d.AddLine("muros", "SHORT", 0, 0.2, 3, 0.2);

        OperationResult<FaceJoin> result = d.AssignMaterialJoiningFaces(["LONG", "SHORT"], "brick", null);

        Assert.Equal(["LONG"], result.Value!.PartlyUnpaired);
        Assert.Contains("LONG", Assert.Single(result.Warnings));
    }

    [Theory]
    [InlineData("nope", null)]
    [InlineData("brick", 0.0)]
    public void Joining_with_invalid_data_changes_nothing(string material, double? thickness)
    {
        FakeDrawing d = TwoFaces();

        OperationResult<FaceJoin> result = d.AssignMaterialJoiningFaces(["F1", "F2"], material, thickness);

        Assert.False(result.Succeeded);
        Assert.Empty(d.Walls);
    }

    [Fact]
    public void The_old_flow_makes_two_walls_and_validation_warns_with_both_handles()
    {
        FakeDrawing d = TwoFaces();

        d.AssignMaterial(["F1", "F2"], "brick", null);
        ValidationSummary summary = d.Validate();
        QueryResult query = QueryAcross(d);

        ValidationItem item = Assert.Single(summary.Items, i => i.Code == ValidationIssue.DoubleCount);
        Assert.Contains("F1", item.Message);
        Assert.Contains("F2", item.Message);
        Assert.Equal(16, query.Calculation.WallLossDb, 9);
        Assert.Equal(["F1", "F2"], Assert.Single(d.FindParallelWalls()).Handles.Order());
    }

    [Fact]
    public void Converting_parallel_walls_leaves_one_axis_and_moves_the_faces_to_the_neutral_layer()
    {
        FakeDrawing d = TwoFaces();
        d.AssignMaterial(["F1", "F2"], "brick", null);

        OperationResult<ParallelConversion> result = d.ConvertParallelWalls();

        Assert.True(result.Succeeded);
        Assert.Equal((1, 2), (result.Value!.AxesCreated, result.Value.FacesMoved));
        Wall axis = Assert.Single(d.Walls.Values);
        Assert.Equal("brick", axis.MaterialId);
        Assert.Equal(0.2, axis.ThicknessM!.Value, 9);
        Assert.Equal(FakeDrawing.FaceLayer, d.Lines["F1"].Layer);
        Assert.Equal(FakeDrawing.FaceLayer, d.Lines["F2"].Layer);
        Assert.Equal(8, QueryAcross(d).Calculation.WallLossDb, 9);
        Assert.Empty(d.FindParallelWalls());
    }

    [Fact]
    public void Converting_uses_the_material_with_the_largest_loss()
    {
        var d = new FakeDrawing();
        d.Add("W1", 5, 0, 5, 8, "gypsum", 0.1);
        d.Add("W2", 5.2, 0, 5.2, 8, "brick", 0.1);

        d.ConvertParallelWalls();

        Assert.Equal("brick", Assert.Single(d.Walls.Values).MaterialId);
    }

    [Fact]
    public void Converting_skips_walls_with_long_unpaired_stretches()
    {
        var d = new FakeDrawing();
        d.Add("LONG", 0, 0, 10, 0, "brick", 0.1);
        d.Add("SHORT", 0, 0.2, 3, 0.2, "brick", 0.1);

        OperationResult<ParallelConversion> result = d.ConvertParallelWalls();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.AxesCreated);
        Assert.Equal(["LONG", "SHORT"], result.Value.Skipped.Order());
        Assert.Contains("LONG", Assert.Single(result.Warnings));
        Assert.Equal(2, d.Walls.Count);
    }

    [Fact]
    public void Converting_without_parallel_walls_changes_nothing()
    {
        var d = new FakeDrawing();
        d.Add("A1", 0, 0, 10, 0, "brick", 0.2);
        d.Add("A2", 0, 8, 10, 8, "brick", 0.2);

        OperationResult<ParallelConversion> result = d.ConvertParallelWalls();

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.FacesMoved);
        Assert.Single(result.Warnings);
        Assert.Equal(2, d.Walls.Count);
    }

    [Fact]
    public void Converting_keeps_a_valid_calculation_only_if_nothing_changed()
    {
        var d = new FakeDrawing();
        d.Add("A1", 0, 0, 10, 0, "brick", 0.2);
        d.Add("A2", 10, 0, 10, 8, "brick", 0.2);
        d.Add("A3", 10, 8, 0, 8, "brick", 0.2);
        d.Add("A4", 0, 8, 0, 0, "brick", 0.2);
        d.SetAccessPoint(new Vec2(2, 4));
        Assert.True(d.Calculate().Succeeded);

        d.ConvertParallelWalls();
        Assert.True(d.HasResult);

        d.Add("B1", 5, 0, 5, 8, "gypsum", 0.1);
        d.Add("B2", 5.1, 0, 5.1, 8, "gypsum", 0.1);
        d.ConvertParallelWalls();
        Assert.False(d.HasResult);
    }
}
