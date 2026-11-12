using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Walls;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Service;

public class DrawingPreparationTests
{
    private static readonly BoundingBox Window = new(new Vec2(-1, -1), new Vec2(11, 9));

    private static FakeDrawing DoubleFaceWall(double openingM = 0)
    {
        var d = new FakeDrawing();
        if (openingM <= 0)
        {
            d.AddFace("muros", "a", 0, 0, 5, 0);
            d.AddFace("muros", "b", 0, 0.2, 5, 0.2);
        }
        else
        {
            d.AddFace("muros", "a1", 0, 0, 2, 0);
            d.AddFace("muros", "b1", 0, 0.2, 2, 0.2);
            d.AddFace("muros", "a2", 2 + openingM, 0, 5, 0);
            d.AddFace("muros", "b2", 2 + openingM, 0.2, 5, 0.2);
        }

        return d;
    }

    private static FakeDrawing CalculatedRoom()
    {
        var d = new FakeDrawing();
        d.Add("A1", 0, 0, 10, 0, "brick", 0.2);
        d.Add("A2", 10, 0, 10, 8, "brick", 0.2);
        d.Add("A3", 10, 8, 0, 8, "brick", 0.2);
        d.Add("A4", 0, 8, 0, 0, "brick", 0.2);
        d.SetAccessPoint(new Vec2(2, 4));
        Assert.True(d.Calculate().Succeeded);
        return d;
    }

    [Fact]
    public void Setup_reports_missing_unit_and_the_suggestion()
    {
        var d = new FakeDrawing { Unit = null };

        DrawingSetup setup = d.GetDrawingSetup();

        Assert.Null(setup.Unit);
        Assert.Equal(DrawingUnit.Millimetres, setup.SuggestedUnit);
    }

    [Fact]
    public void SetDrawingUnit_stores_the_unit_and_clears_the_units_error()
    {
        var d = new FakeDrawing { Unit = null };
        d.Add("A1", 0, 0, 10, 0, "brick", 0.2);
        Assert.Contains(d.Validate().Items, i => i.Code == ValidationIssue.Units);

        OperationResult<DrawingUnit> result = d.SetDrawingUnit(DrawingUnit.Centimetres);

        Assert.True(result.Succeeded);
        Assert.Equal(DrawingUnit.Centimetres, d.GetDrawingSetup().Unit);
        Assert.DoesNotContain(d.Validate().Items, i => i.Code == ValidationIssue.Units);
    }

    [Fact]
    public void SetDrawingUnit_rejects_an_unknown_unit()
    {
        var d = new FakeDrawing { Unit = null };

        Assert.False(d.SetDrawingUnit((DrawingUnit)99).Succeeded);
        Assert.Null(d.Unit);
    }

    [Fact]
    public void Survey_lists_layers_with_segment_and_axis_counts()
    {
        FakeDrawing d = DoubleFaceWall();
        d.AddFace("muebles", "m1", 3, 3, 4, 3);

        AxisSurvey survey = d.SurveyAxisLayers(Window).Value!;

        AxisLayerInfo walls = Assert.Single(survey.Layers, l => l.Layer == "muros");
        Assert.Equal(2, walls.SegmentCount);
        Assert.Equal(1, walls.AxisCount);
        AxisLayerInfo furniture = Assert.Single(survey.Layers, l => l.Layer == "muebles");
        Assert.Equal(1, furniture.SegmentCount);
        Assert.Equal(0, furniture.AxisCount);
    }

    [Fact]
    public void Survey_suggests_the_layer_named_muros_first()
    {
        FakeDrawing d = DoubleFaceWall();
        d.AddFace("tabiques", "t1", 0, 3, 5, 3);
        d.AddFace("tabiques", "t2", 0, 3.1, 5, 3.1);
        d.AddFace("tabiques", "t3", 0, 5, 5, 5);
        d.AddFace("tabiques", "t4", 0, 5.1, 5, 5.1);

        AxisSurvey survey = d.SurveyAxisLayers(Window).Value!;

        Assert.Equal("muros", survey.SuggestedLayer);
        Assert.Equal("muros", survey.Layers[0].Layer);
    }

    [Fact]
    public void Survey_never_suggests_layer_0_or_ventanas()
    {
        var d = new FakeDrawing();
        foreach (string layer in new[] { "0", "Ventanas" })
        {
            d.AddFace(layer, layer + "a", 0, 0, 5, 0);
            d.AddFace(layer, layer + "b", 0, 0.2, 5, 0.2);
        }

        AxisSurvey survey = d.SurveyAxisLayers(Window).Value!;

        Assert.Null(survey.SuggestedLayer);
        Assert.All(survey.Layers, l => Assert.True(l.ExplicitOnly));
    }

    [Fact]
    public void Survey_without_muros_suggests_a_wall_layer_and_lists_explicit_only_layers_last()
    {
        var d = new FakeDrawing();
        d.AddFace("0", "za", 0, 0, 5, 0);
        d.AddFace("0", "zb", 0, 0.2, 5, 0.2);
        d.AddFace("A-WALL", "wa", 0, 3, 5, 3);
        d.AddFace("A-WALL", "wb", 0, 3.2, 5, 3.2);

        AxisSurvey survey = d.SurveyAxisLayers(Window).Value!;

        Assert.Equal("A-WALL", survey.SuggestedLayer);
        Assert.Equal(["A-WALL", "0"], survey.Layers.Select(l => l.Layer));
    }

    [Fact]
    public void Survey_of_an_empty_window_fails()
    {
        FakeDrawing d = DoubleFaceWall();

        OperationResult<AxisSurvey> result = d.SurveyAxisLayers(new BoundingBox(new Vec2(50, 50), new Vec2(60, 60)));

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void GenerateAxes_without_unit_fails()
    {
        FakeDrawing d = DoubleFaceWall();
        d.Unit = null;

        Assert.False(d.GenerateAxes(Window, ["muros"]).Succeeded);
        Assert.Empty(d.Walls);
    }

    [Fact]
    public void GenerateAxes_creates_marked_walls_without_material_and_with_thickness()
    {
        FakeDrawing d = DoubleFaceWall();

        AxisGeneration result = d.GenerateAxes(Window, ["muros"]).Value!;

        Assert.Equal(1, result.Created);
        Assert.Equal(2, result.SegmentCount);
        Wall wall = Assert.Single(d.Walls.Values);
        Assert.Null(wall.MaterialId);
        Assert.Equal(0.2, wall.ThicknessM);
        Assert.Contains(wall.Id, d.GeneratedHandles);
        Assert.Single(d.LoadWalls());
    }

    [Fact]
    public void GenerateAxes_twice_on_the_same_window_keeps_the_same_axes()
    {
        FakeDrawing d = DoubleFaceWall();
        d.GenerateAxes(Window, ["muros"]);
        string handle = Assert.Single(d.Walls.Keys);

        AxisGeneration second = d.GenerateAxes(Window, ["muros"]).Value!;

        Assert.Equal(0, second.Created);
        Assert.Equal(1, second.Kept);
        Assert.Equal(0, second.Removed);
        Assert.Equal(handle, Assert.Single(d.Walls.Keys));
    }

    [Fact]
    public void GenerateAxes_again_keeps_the_material_already_assigned()
    {
        FakeDrawing d = DoubleFaceWall();
        d.GenerateAxes(Window, ["muros"]);
        d.AssignMaterial([.. d.Walls.Keys], "brick", null);

        d.GenerateAxes(Window, ["muros"]);

        Assert.Equal("brick", Assert.Single(d.Walls.Values).MaterialId);
    }

    [Fact]
    public void GenerateAxes_with_another_merge_gap_replaces_the_previous_axes()
    {
        FakeDrawing d = DoubleFaceWall(openingM: 0.3);
        AxisGeneration open = d.GenerateAxes(Window, ["muros"]).Value!;
        Assert.Equal(2, open.AxisCount);

        AxisGeneration closed = d.GenerateAxes(Window, ["muros"], mergeGapM: 0.5).Value!;

        Assert.Equal(1, closed.AxisCount);
        Assert.Equal(1, closed.Created);
        Assert.Equal(2, closed.Removed);
        Assert.Equal(5, Assert.Single(d.Walls.Values).Segments[0].Length, 6);
    }

    [Theory]
    [InlineData(0.15, 1)]
    [InlineData(0.30, 2)]
    public void Default_merge_gap_closes_only_openings_up_to_20_cm(double openingM, int expectedAxes)
    {
        FakeDrawing d = DoubleFaceWall(openingM);

        AxisGeneration result = d.GenerateAxes(Window, ["muros"]).Value!;

        Assert.Equal(0.2, AxisDefaults.MergeGapM);
        Assert.Equal(expectedAxes, result.AxisCount);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(2.5)]
    [InlineData(double.NaN)]
    public void GenerateAxes_rejects_a_merge_gap_out_of_range(double gap)
    {
        FakeDrawing d = DoubleFaceWall();

        Assert.False(d.GenerateAxes(Window, ["muros"], gap).Succeeded);
        Assert.Empty(d.Walls);
    }

    [Fact]
    public void GenerateAxes_with_a_layer_that_has_nothing_in_the_window_changes_nothing()
    {
        FakeDrawing d = DoubleFaceWall();

        OperationResult<AxisGeneration> result = d.GenerateAxes(Window, ["muros", "tabiques"]);

        Assert.False(result.Succeeded);
        Assert.Contains("tabiques", result.Errors[0]);
        Assert.Empty(d.Walls);
    }

    [Fact]
    public void GenerateAxes_without_layers_fails()
    {
        FakeDrawing d = DoubleFaceWall();

        Assert.False(d.GenerateAxes(Window, [" "]).Succeeded);
    }

    [Fact]
    public void GenerateAxes_processes_layer_0_only_when_it_is_named()
    {
        var d = new FakeDrawing();
        d.AddFace("0", "za", 0, 0, 5, 0);
        d.AddFace("0", "zb", 0, 0.2, 5, 0.2);

        AxisGeneration result = d.GenerateAxes(Window, ["0"]).Value!;

        Assert.Equal(1, result.Created);
    }

    [Fact]
    public void GenerateAxes_leaves_generated_axes_outside_the_window()
    {
        FakeDrawing d = DoubleFaceWall();
        d.AddFace("muros", "c", 20, 0, 25, 0);
        d.AddFace("muros", "d", 20, 0.2, 25, 0.2);
        d.GenerateAxes(new BoundingBox(new Vec2(19, -1), new Vec2(26, 1)), ["muros"]);

        AxisGeneration result = d.GenerateAxes(Window, ["muros"]).Value!;

        Assert.Equal(1, result.Created);
        Assert.Equal(0, result.Removed);
        Assert.Equal(2, d.Walls.Count);
    }

    [Fact]
    public void GenerateAxes_does_not_touch_walls_drawn_by_hand()
    {
        FakeDrawing d = DoubleFaceWall();
        d.Add("H1", 0, 0.1, 5, 0.1, "brick", 0.2);

        AxisGeneration result = d.GenerateAxes(Window, ["muros"]).Value!;

        Assert.Equal(1, result.Created);
        Assert.Contains("H1", d.Walls.Keys);
    }

    [Fact]
    public void GenerateAxes_without_pairs_warns_and_keeps_previous_axes()
    {
        FakeDrawing d = DoubleFaceWall();
        d.GenerateAxes(Window, ["muros"]);
        d.AddFace("muebles", "m1", 3, 3, 4, 3);

        OperationResult<AxisGeneration> result = d.GenerateAxes(Window, ["muebles"]);

        Assert.True(result.Succeeded);
        Assert.NotEmpty(result.Warnings);
        Assert.Equal(0, result.Value!.AxisCount);
        Assert.Single(d.Walls);
    }

    [Fact]
    public void GenerateAxes_invalidates_the_previous_result()
    {
        FakeDrawing d = CalculatedRoom();
        d.AddFace("muros", "a", 2, 2, 5, 2);
        d.AddFace("muros", "b", 2, 2.2, 5, 2.2);

        d.GenerateAxes(Window, ["muros"]);

        Assert.False(d.HasResult);
    }

    [Fact]
    public void Coverage_area_defaults_to_the_wall_rectangle()
    {
        CoverageAreaInfo area = new FakeDrawing().GetCoverageArea();

        Assert.False(area.IsCustom);
        Assert.Null(area.Handle);
    }

    [Fact]
    public void SetCoverageArea_uses_the_polyline_and_describes_it()
    {
        FakeDrawing d = CalculatedRoom();
        d.Polylines["2F"] = new Polygon2([new Vec2(1, 1), new Vec2(4, 1), new Vec2(4, 3), new Vec2(1, 3)]);

        OperationResult<CoverageAreaInfo> result = d.SetCoverageArea(" 2F ");

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.IsCustom);
        Assert.Equal("2F", result.Value.Handle);
        Assert.Equal(4, result.Value.VertexCount);
        Assert.Equal(6, result.Value.AreaM2, 9);
        Assert.False(d.HasResult);
        Assert.Equal(24, d.Calculate().Value!.CellsWithValue);
        Assert.True(d.GetCoverageArea().IsCustom);
    }

    [Fact]
    public void SetCoverageArea_with_an_unknown_handle_changes_nothing()
    {
        FakeDrawing d = CalculatedRoom();

        OperationResult<CoverageAreaInfo> result = d.SetCoverageArea("FFFF");

        Assert.False(result.Succeeded);
        Assert.True(d.HasResult);
        Assert.False(d.GetCoverageArea().IsCustom);
    }

    [Fact]
    public void SetCoverageArea_without_unit_fails()
    {
        var d = new FakeDrawing { Unit = null };
        d.Polylines["2F"] = new Polygon2([new Vec2(1, 1), new Vec2(4, 1), new Vec2(4, 3)]);

        Assert.False(d.SetCoverageArea("2F").Succeeded);
        Assert.Null(d.Area);
    }

    [Fact]
    public void ClearCoverageArea_returns_to_the_wall_rectangle()
    {
        FakeDrawing d = CalculatedRoom();
        d.Polylines["2F"] = new Polygon2([new Vec2(1, 1), new Vec2(4, 1), new Vec2(4, 3), new Vec2(1, 3)]);
        d.SetCoverageArea("2F");

        OperationResult<CoverageAreaInfo> result = d.ClearCoverageArea();

        Assert.True(result.Succeeded);
        Assert.False(result.Value!.IsCustom);
        Assert.Equal(320, d.Calculate().Value!.CellsWithValue);
    }

    [Fact]
    public void ClearCoverageArea_without_custom_area_keeps_the_result()
    {
        FakeDrawing d = CalculatedRoom();

        d.ClearCoverageArea();

        Assert.True(d.HasResult);
    }

    [Fact]
    public void SetOverlayFade_updates_the_drawn_map_without_recalculating()
    {
        FakeDrawing d = CalculatedRoom();

        OperationResult<int> result = d.SetOverlayFade(70);

        Assert.True(result.Succeeded);
        Assert.Equal(70, d.Settings.OverlayFadePercent);
        Assert.Equal([70], d.FadesApplied);
        Assert.Equal(1, d.LegendRedraws);
        Assert.Equal(1, d.Draws);
        Assert.True(d.HasResult);
    }

    [Fact]
    public void SetOverlayFade_out_of_range_changes_nothing()
    {
        FakeDrawing d = CalculatedRoom();

        Assert.False(d.SetOverlayFade(95).Succeeded);
        Assert.Empty(d.FadesApplied);
        Assert.Equal(OverlayFade.DefaultPercent, d.Settings.OverlayFadePercent);
    }

    [Fact]
    public void SetOverlayFade_before_any_calculation_is_remembered()
    {
        var d = new FakeDrawing();

        d.SetOverlayFade(10);

        Assert.Equal(10, d.Settings.OverlayFadePercent);
        Assert.Equal(0, d.LegendRedraws);
    }

    [Fact]
    public void Result_stays_current_while_the_drawing_does_not_change()
    {
        FakeDrawing d = CalculatedRoom();

        d.LoadWalls();
        d.Validate();

        Assert.True(d.HasResult);
    }

    [Fact]
    public void Result_is_outdated_after_a_wall_changes()
    {
        FakeDrawing d = CalculatedRoom();
        d.Add("B1", 5, 0, 5, 8, "gypsum", 0.1);

        d.LoadWalls();

        Assert.False(d.HasResult);
    }

    [Fact]
    public void Materials_lists_the_catalog()
    {
        Assert.Equal(["brick", "gypsum"], new FakeDrawing().Materials.Select(m => m.Id).Order());
    }

    [Fact]
    public void SuggestedExportPath_comes_from_the_drawing()
    {
        Assert.EndsWith("-wifiheat.png", new FakeDrawing().SuggestedExportPath);
    }
}
