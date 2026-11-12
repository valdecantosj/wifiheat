using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Tests.Service;
using Xunit;

namespace WifiHeat.AutoCAD.UI.Tests;

public class PrepareAndValidationTests
{
    private static readonly BoundingBox Window = new(new Vec2(-1, -1), new Vec2(11, 9));

    private static Sample WithFaces()
    {
        Sample s = Sample.Custom(d =>
        {
            d.AddFace("muros", "a", 0, 0, 5, 0);
            d.AddFace("muros", "b", 0, 0.2, 5, 0.2);
            d.AddFace("muros", "c", 0, 3, 5, 3);
            d.AddFace("0", "za", 0, 5, 5, 5);
            d.AddFace("0", "zb", 0, 5.2, 5, 5.2);
            d.AddFace("puertas", "p1", 1, 6, 2, 6);
        });
        s.Interaction.Window = Window;
        return s;
    }

    [Fact]
    public void Without_unit_the_section_asks_for_it()
    {
        Sample s = Sample.Custom(d => d.Unit = null);

        Assert.False(s.Panel.Prepare.HasUnit);
        Assert.Null(s.Panel.Prepare.SelectedUnit);
        Assert.Contains("Falta elegir la unidad", s.Panel.Prepare.UnitHint);
        Assert.Contains("milímetros", s.Panel.Prepare.UnitHint);
        Assert.Equal("Falta elegir la unidad del dibujo.", s.Panel.Calculation.Readiness);
    }

    [Fact]
    public async Task Without_unit_nothing_is_asked_in_the_drawing()
    {
        Sample s = Sample.Custom(d => d.Unit = null);
        s.Interaction.Window = Window;
        s.Interaction.Points.Enqueue(new Vec2(1, 1));
        s.Interaction.Polyline = "2F";

        await s.Panel.Prepare.PickWindowAsync();
        await s.Panel.AccessPoint.PickAsync();
        await s.Panel.Query.QueryAsync();
        await s.Panel.Calculation.PickAreaAsync();
        s.Panel.AccessPoint.XText = "1";
        s.Panel.AccessPoint.YText = "2";

        Assert.Empty(s.Interaction.Prompts);
        Assert.Null(s.Drawing.AccessPoint);
        Assert.Equal("", s.Panel.AccessPoint.XText);
        Assert.Equal(StatusKind.Warning, s.Panel.StatusKind);
        Assert.Contains("unidad", s.Panel.StatusMessage);
    }

    [Fact]
    public void Counts_use_singular_and_plural()
    {
        Sample s = Sample.Custom(d => d.Add("A1", 0, 0, 5, 0, null, 0.2));

        Assert.Equal("1 muro", s.Panel.Validation.WallsText);
        Assert.Equal("1 error", s.Panel.Validation.ErrorsText);
        Assert.Equal("0 avisos", s.Panel.Validation.WarningsText);
    }

    [Fact]
    public void Choosing_a_unit_saves_it_in_the_drawing()
    {
        Sample s = Sample.Custom(d => d.Unit = null);

        s.Panel.Prepare.SelectedUnit = s.Panel.Prepare.Units.Single(u => u.Unit == DrawingUnit.Centimetres);

        Assert.Equal(DrawingUnit.Centimetres, s.Drawing.Unit);
        Assert.True(s.Panel.Prepare.HasUnit);
        Assert.Equal(StatusKind.Success, s.Panel.StatusKind);
    }

    [Fact]
    public void Stored_unit_is_shown_without_writing_again()
    {
        Sample s = Sample.Room();

        Assert.Equal(DrawingUnit.Metres, s.Panel.Prepare.SelectedUnit!.Unit);
        Assert.Equal("", s.Panel.StatusMessage);
    }

    [Fact]
    public async Task Picking_a_window_lists_layers_with_counts_and_checks_only_the_suggested_one()
    {
        Sample s = WithFaces();

        await s.Panel.Prepare.PickWindowAsync();

        PrepareViewModel prepare = s.Panel.Prepare;
        Assert.Equal(["muros", "puertas", "0"], prepare.Layers.Select(l => l.Name));
        AxisLayerViewModel walls = prepare.Layers[0];
        Assert.True(walls.IsSelected);
        Assert.Equal(3, walls.SegmentCount);
        Assert.Equal(1, walls.AxisCount);
        Assert.Equal("sugerida", walls.Note);
        AxisLayerViewModel zero = prepare.Layers[2];
        Assert.False(zero.IsSelected);
        Assert.Equal("solo si se elige", zero.Note);
        Assert.True(prepare.HasWindow);
        Assert.True(prepare.CanGenerate);
        Assert.False(s.Panel.IsInteracting);
    }

    [Fact]
    public async Task Cancelling_the_window_changes_nothing()
    {
        Sample s = WithFaces();
        s.Interaction.Window = null;

        await s.Panel.Prepare.PickWindowAsync();

        Assert.False(s.Panel.Prepare.HasWindow);
        Assert.Equal(StatusKind.Info, s.Panel.StatusKind);
    }

    [Fact]
    public async Task An_empty_window_reports_the_error()
    {
        Sample s = WithFaces();
        s.Interaction.Window = new BoundingBox(new Vec2(50, 50), new Vec2(60, 60));

        await s.Panel.Prepare.PickWindowAsync();

        Assert.False(s.Panel.Prepare.HasWindow);
        Assert.Equal(StatusKind.Error, s.Panel.StatusKind);
    }

    [Fact]
    public async Task Unchecking_every_layer_disables_generation()
    {
        Sample s = WithFaces();
        await s.Panel.Prepare.PickWindowAsync();

        s.Panel.Prepare.Layers[0].IsSelected = false;

        Assert.False(s.Panel.Prepare.CanGenerate);
        s.Panel.Prepare.GenerateAxes();
        Assert.Empty(s.Drawing.Walls);
        Assert.Equal(StatusKind.Warning, s.Panel.StatusKind);
    }

    [Fact]
    public void Generating_without_a_window_asks_for_it()
    {
        Sample s = WithFaces();

        s.Panel.Prepare.GenerateAxes();

        Assert.Empty(s.Drawing.Walls);
        Assert.Equal(StatusKind.Warning, s.Panel.StatusKind);
    }

    [Fact]
    public async Task Generating_axes_fills_the_wall_table_and_reports_unpaired_segments()
    {
        Sample s = WithFaces();
        await s.Panel.Prepare.PickWindowAsync();

        s.Panel.Prepare.GenerateAxes();

        WallRowViewModel row = Assert.Single(s.Panel.Walls.Rows);
        Assert.True(row.MissingMaterial);
        Assert.Equal("0,2", row.ThicknessText);
        Assert.Contains("1 eje(s): 1 creado(s)", s.Panel.Prepare.AxisSummary);
        Assert.True(s.Panel.Prepare.HasUnpaired);
        s.Panel.Prepare.ShowUnpaired();
        Assert.Equal(["c"], s.Interaction.Shown.Single().Handles);
    }

    [Fact]
    public async Task Generating_twice_keeps_the_existing_axes()
    {
        Sample s = WithFaces();
        await s.Panel.Prepare.PickWindowAsync();
        s.Panel.Prepare.GenerateAxes();

        s.Panel.Prepare.GenerateAxes();

        Assert.Single(s.Panel.Walls.Rows);
        Assert.Contains("0 creado(s), 1 conservado(s)", s.Panel.Prepare.AxisSummary);
    }

    [Fact]
    public async Task Explicitly_chosen_layer_0_is_processed()
    {
        Sample s = WithFaces();
        await s.Panel.Prepare.PickWindowAsync();
        s.Panel.Prepare.Layers.Single(l => l.Name == "0").IsSelected = true;

        s.Panel.Prepare.GenerateAxes();

        Assert.Equal(2, s.Panel.Walls.Rows.Count);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("-1")]
    [InlineData("3")]
    public async Task Invalid_merge_gap_is_rejected_before_touching_the_drawing(string text)
    {
        Sample s = WithFaces();
        await s.Panel.Prepare.PickWindowAsync();
        s.Panel.Prepare.MergeGapText = text;

        s.Panel.Prepare.GenerateAxes();

        Assert.Empty(s.Drawing.Walls);
        Assert.Equal(StatusKind.Error, s.Panel.StatusKind);
    }

    [Fact]
    public async Task Merge_gap_defaults_to_20_cm_and_accepts_a_decimal_comma()
    {
        Sample s = Sample.Custom(d =>
        {
            d.AddFace("muros", "a1", 0, 0, 2, 0);
            d.AddFace("muros", "b1", 0, 0.2, 2, 0.2);
            d.AddFace("muros", "a2", 2.3, 0, 5, 0);
            d.AddFace("muros", "b2", 2.3, 0.2, 5, 0.2);
        });
        s.Interaction.Window = Window;
        Assert.Equal("0,20", s.Panel.Prepare.MergeGapText);

        s.Panel.Prepare.MergeGapText = "0,5";
        await s.Panel.Prepare.PickWindowAsync();
        s.Panel.Prepare.GenerateAxes();

        Assert.Single(s.Panel.Walls.Rows);
    }

    [Fact]
    public void Validation_lists_problems_with_handles_and_counts()
    {
        Sample s = Sample.Room(material: null);

        ValidationViewModel v = s.Panel.Validation;
        Assert.True(v.HasRun);
        Assert.Equal(5, v.WallCount);
        Assert.Equal(5, v.ErrorCount);
        Assert.False(v.IsValid);
        Assert.Equal("5 muro(s) · 5 error(es) · 0 aviso(s)", v.Summary);
        ValidationItemViewModel item = Assert.Single(v.Items, i => i.Handle == "B1");
        Assert.True(item.IsError);
        Assert.Equal("Error", item.SeverityLabel);
        Assert.Equal("FALTA PROPIEDAD · B1", item.Title);
    }

    [Fact]
    public void Selecting_a_problem_zooms_to_the_wall_and_focuses_its_row()
    {
        Sample s = Sample.Room(material: null);
        s.Panel.Walls.ShowSelected = true;

        s.Panel.Validation.SelectedItem = s.Panel.Validation.Items.Single(i => i.Handle == "A3");

        (string[] handles, bool zoom) = Assert.Single(s.Interaction.Shown);
        Assert.Equal(["A3"], handles);
        Assert.True(zoom);
        Assert.Equal("A3", s.Panel.Walls.FocusedRow!.Handle);
        Assert.True(s.Row("A3").IsFocused);
        Assert.True(s.Panel.Walls.ShowAll);
    }

    [Fact]
    public void A_problem_without_handle_does_not_touch_the_drawing()
    {
        Sample s = Sample.Empty();

        s.Panel.Validation.SelectedItem = s.Panel.Validation.Items.First(i => !i.HasHandle);

        Assert.Empty(s.Interaction.Shown);
    }

    [Fact]
    public void Validate_reports_a_clean_plan()
    {
        Sample s = Sample.Room();

        s.Panel.Validation.Validate();

        Assert.True(s.Panel.Validation.IsValid);
        Assert.False(s.Panel.Validation.HasItems);
        Assert.Equal(StatusKind.Success, s.Panel.StatusKind);
        Assert.Contains("5 muro(s)", s.Panel.StatusMessage);
    }

    [Fact]
    public void Validate_rereads_the_drawing()
    {
        Sample s = Sample.Room();
        s.Drawing.Add("C9", 2, 2, 3, 2, null, 0.1);

        s.Panel.Validation.Validate();

        Assert.Equal(1, s.Panel.Validation.ErrorCount);
        Assert.Equal(StatusKind.Error, s.Panel.StatusKind);
        Assert.Equal(6, s.Panel.Walls.Rows.Count);
    }
}
