using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Service;

public class WifiHeatServiceTests
{
    private static FakeDrawing Room(string? material = "brick")
    {
        var d = new FakeDrawing();
        d.Add("A1", 0, 0, 10, 0, material, 0.2);
        d.Add("A2", 10, 0, 10, 8, material, 0.2);
        d.Add("A3", 10, 8, 0, 8, material, 0.2);
        d.Add("A4", 0, 8, 0, 0, material, 0.2);
        d.Add("B1", 5, 0, 5, 8, material, 0.1);
        return d;
    }

    [Fact]
    public void LoadWalls_reports_handle_length_layer_and_properties()
    {
        FakeDrawing d = Room();

        WallInfo wall = Assert.Single(d.LoadWalls(), w => w.Handle == "A2");

        Assert.Equal(8, wall.LengthM, 9);
        Assert.Equal("WIFIHEAT_MUROS", wall.Layer);
        Assert.Equal("brick", wall.MaterialId);
        Assert.Equal(0.2, wall.ThicknessM);
    }

    [Fact]
    public void Validate_reports_missing_material_with_the_wall_handle()
    {
        FakeDrawing d = Room(material: null);

        ValidationSummary summary = d.Validate();

        Assert.False(summary.IsValid);
        Assert.Equal(5, summary.ErrorCount);
        Assert.Contains(summary.Items, i => i.Code == ValidationIssue.MissingProperty && i.Handle == "B1");
        Assert.Equal(new BoundingBox(new Vec2(0, 0), new Vec2(10, 8)), summary.WallBounds);
    }

    [Fact]
    public void AssignMaterial_fixes_validation_and_keeps_thickness_when_none_is_given()
    {
        FakeDrawing d = Room(material: null);

        OperationResult<int> result = d.AssignMaterial(["A1", "A2", "A3", "A4", "B1"], "gypsum", null);

        Assert.Equal(5, result.Value);
        Assert.True(d.Validate().IsValid);
        Assert.Equal(0.1, d.Walls["B1"].ThicknessM);
        Assert.Equal("gypsum", d.Walls["A1"].MaterialId);
    }

    [Fact]
    public void AssignMaterial_uses_the_given_thickness()
    {
        FakeDrawing d = Room();

        d.AssignMaterial(["B1"], "BRICK", 0.3);

        Assert.Equal(0.3, d.Walls["B1"].ThicknessM);
        Assert.Equal("brick", d.Walls["B1"].MaterialId);
    }

    [Fact]
    public void AssignMaterial_rejects_unknown_material_without_writing()
    {
        FakeDrawing d = Room(material: null);

        OperationResult<int> result = d.AssignMaterial(["A1"], "plomo", 0.2);

        Assert.False(result.Succeeded);
        Assert.Contains(ValidationIssue.UnknownMaterial, Assert.Single(result.Errors));
        Assert.Null(d.Walls["A1"].MaterialId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.1)]
    [InlineData(double.NaN)]
    public void AssignMaterial_rejects_invalid_thickness(double thickness)
    {
        FakeDrawing d = Room(material: null);

        Assert.False(d.AssignMaterial(["A1"], "brick", thickness).Succeeded);
        Assert.Null(d.Walls["A1"].MaterialId);
    }

    [Fact]
    public void AssignMaterial_reports_handles_that_do_not_exist()
    {
        FakeDrawing d = Room(material: null);

        OperationResult<int> result = d.AssignMaterial(["A1", "FFFF"], "brick", null);

        Assert.Equal(1, result.Value);
        Assert.Contains("FFFF", Assert.Single(result.Warnings));
    }

    [Fact]
    public void Calculate_without_access_point_fails_and_draws_nothing()
    {
        FakeDrawing d = Room();

        OperationResult<CalculationSummary> result = d.Calculate();

        Assert.False(result.Succeeded);
        Assert.Contains("AP", Assert.Single(result.Errors));
        Assert.Equal(0, d.Draws);
    }

    [Fact]
    public void Calculate_with_validation_errors_lists_them_with_handles()
    {
        FakeDrawing d = Room();
        d.Add("C1", 1, 1, 2, 1, "plomo", 0.1);
        d.SetAccessPoint(new Vec2(2, 4));

        OperationResult<CalculationSummary> result = d.Calculate();

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("C1") && e.Contains(ValidationIssue.UnknownMaterial));
    }

    [Fact]
    public void Calculate_draws_once_and_summarises_the_grid()
    {
        FakeDrawing d = Room();
        d.SetAccessPoint(new Vec2(2, 4));

        CalculationSummary summary = d.Calculate().Value!;

        Assert.Equal(20 * 16, summary.Cells);
        Assert.Equal(0.5, summary.CellSizeM);
        Assert.True(summary.MaxDbm > summary.MinDbm);
        Assert.Equal("overlay_001.png", summary.ImagePath);
        Assert.Equal(1, d.Draws);
    }

    [Fact]
    public void Calculate_rereads_the_drawing()
    {
        FakeDrawing d = Room();
        d.SetAccessPoint(new Vec2(2, 4));
        d.Calculate();

        d.Walls.Remove("B1");
        CalculationSummary summary = d.Calculate().Value!;

        Assert.Equal(4, d.Session.Walls.Count);
        Assert.Equal(2, d.Draws);
        Assert.NotNull(summary);
    }

    [Fact]
    public void SetAccessPoint_saves_the_marker_with_the_current_settings()
    {
        FakeDrawing d = Room();

        AccessPointInfo ap = d.SetAccessPoint(new Vec2(3, 3)).Value!;

        Assert.Equal(new Vec2(3, 3), ap.PositionM);
        Assert.Equal(new Vec2(3, 3), Assert.Single(d.SavedAccessPoints).Ap.Position);
    }

    [Fact]
    public void Configure_applies_all_settings_and_updates_the_saved_access_point()
    {
        FakeDrawing d = Room();
        d.SetAccessPoint(new Vec2(3, 3));

        OperationResult<SimulationSettings> result = d.Configure(new SimulationSettings(Band.Ghz5, 17, 2, 1, 0.5, 0.3, 0.25, 20));

        Assert.True(result.Succeeded);
        Assert.Equal(new SimulationSettings(Band.Ghz5, 17, 2, 1, 0.5, 0.3, 0.25, 20), d.Settings);
        Assert.Equal(Band.Ghz5, d.SavedAccessPoints[^1].Settings.Band);
        Assert.Equal(2, d.SavedAccessPoints.Count);
    }

    [Fact]
    public void Configure_with_any_invalid_value_changes_nothing()
    {
        FakeDrawing d = Room();
        SimulationSettings before = d.Settings;

        OperationResult<SimulationSettings> result = d.Configure(before with { CellSizeM = 0.05, TxCableLossDb = -1 });

        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(before, d.Settings);
    }

    [Fact]
    public void Resolution_from_Configure_is_used_by_Calculate()
    {
        FakeDrawing d = Room();
        d.SetAccessPoint(new Vec2(2, 4));
        d.Configure(d.Settings with { CellSizeM = 0.25 });

        Assert.Equal(40 * 32, d.Calculate().Value!.Cells);
    }

    [Fact]
    public void Query_explains_the_same_value_as_the_session()
    {
        FakeDrawing d = Room();
        d.SetAccessPoint(new Vec2(2, 4));

        QueryResult q = d.Query(new Vec2(8, 4)).Value!;

        Assert.Single(q.Calculation.Walls);
        Assert.Equal(SignalScale.Classify(q.ReceivedPowerDbm), q.Level);
        Assert.Contains("Muros cruzados: 1", q.Explanation);
    }

    [Fact]
    public void Query_without_access_point_fails()
    {
        Assert.False(Room().Query(new Vec2(1, 1)).Succeeded);
    }

    [Fact]
    public void ExportPng_requires_a_calculation()
    {
        FakeDrawing d = Room();
        d.SetAccessPoint(new Vec2(2, 4));

        Assert.False(d.ExportPng("mapa.png").Succeeded);
        Assert.Empty(d.Exported);
    }

    [Fact]
    public void ExportPng_adds_the_extension_and_reports_the_full_path()
    {
        FakeDrawing d = Room();
        d.SetAccessPoint(new Vec2(2, 4));
        d.Calculate();

        string path = d.ExportPng(Path.Combine(Path.GetTempPath(), "mapa")).Value!;

        Assert.EndsWith("mapa.png", path);
        Assert.True(Path.IsPathRooted(path));
        Assert.Equal([path], d.Exported);
    }

    [Fact]
    public void ExportPng_rejects_other_extensions()
    {
        FakeDrawing d = Room();
        d.SetAccessPoint(new Vec2(2, 4));
        d.Calculate();

        Assert.False(d.ExportPng("mapa.jpg").Succeeded);
    }

    [Fact]
    public void ExportPng_turns_io_errors_into_messages()
    {
        FakeDrawing d = Room();
        d.SetAccessPoint(new Vec2(2, 4));
        d.Calculate();
        d.ExportFailure = new IOException("archivo en uso");

        OperationResult<string> result = d.ExportPng("mapa.png");

        Assert.False(result.Succeeded);
        Assert.Contains("archivo en uso", Assert.Single(result.Errors));
    }

    [Fact]
    public void No_walls_is_a_validation_error()
    {
        ValidationSummary summary = new FakeDrawing().Validate();

        Assert.Contains(summary.Items, i => i.Code == ValidationIssue.NoWalls);
    }

    [Fact]
    public void Host_issues_reach_the_summary()
    {
        FakeDrawing d = Room();
        d.HostIssues.Add(DrawingIssues.MissingUnit());

        Assert.Contains(d.Validate().Items, i => i.Code == ValidationIssue.Units);
    }

    [Fact]
    public void Small_coverage_area_only_fills_its_own_cells()
    {
        FakeDrawing d = Room();
        d.SetAccessPoint(new Vec2(2, 4));
        d.Area = new Polygon2([new Vec2(1, 1), new Vec2(2, 1), new Vec2(2, 2), new Vec2(1, 2)]);

        CalculationSummary summary = d.Calculate().Value!;

        Assert.Equal(4, summary.CellsWithValue);
    }
}
