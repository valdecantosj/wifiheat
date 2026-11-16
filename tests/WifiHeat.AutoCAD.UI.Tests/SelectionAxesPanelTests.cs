using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.Core.Geometry;
using Xunit;

namespace WifiHeat.AutoCAD.UI.Tests;

public class SelectionAxesPanelTests
{
    private static Sample WithLooseFaces() => Sample.Custom(d =>
    {
        d.AddLine("muros", "F1", 5, 0, 5, 8);
        d.AddLine("0", "F2", 5.2, 0, 5.2, 8);
        d.AddLine("0", "LOOSE", 8, 0, 8, 8);
    });

    [Fact]
    public async Task Picking_lines_generates_axes_between_the_chosen_ones()
    {
        Sample s = WithLooseFaces();
        s.Interaction.Entities = ["F1", "F2", "LOOSE"];

        await s.Panel.Prepare.PickLinesAsync();

        Assert.Single(s.Drawing.Walls);
        Assert.Single(s.Panel.Walls.Rows);
        Assert.Equal(StatusKind.Success, s.Panel.Messages.Kind);
        Assert.Equal("1 eje(s) de muro generados", s.Panel.Messages.Current!.Title);
        Assert.StartsWith("1 eje(s): 1 creado(s), 0 conservado(s)", s.Panel.Prepare.AxisSummary);
        Assert.Equal("Ver 1 segmento(s) sin par", s.Panel.Prepare.UnpairedText);
        Assert.Equal([false], s.Interaction.HatchRequests);
        Assert.Null(s.Panel.ActiveMode);
    }

    [Fact]
    public void The_mode_is_called_elegir_lineas()
    {
        Assert.Equal("MODO: ELEGIR LÍNEAS", PanelMode.PickFaces.Title);
        Assert.Contains("de cualquier capa", PanelMode.PickFaces.Instruction);
    }

    [Fact]
    public async Task A_selection_without_pairs_is_a_warning_and_changes_nothing()
    {
        Sample s = WithLooseFaces();
        s.Interaction.Entities = ["F1", "LOOSE"];

        await s.Panel.Prepare.PickLinesAsync();

        Assert.Empty(s.Drawing.Walls);
        Assert.Equal(StatusKind.Warning, s.Panel.Messages.Kind);
        Assert.Equal("No se generaron ejes", s.Panel.Messages.Current!.Title);
        Assert.Contains("entre las líneas elegidas", s.Panel.Messages.Current.Detail);
    }

    [Fact]
    public async Task Cancelling_or_choosing_nothing_changes_nothing()
    {
        Sample s = WithLooseFaces();

        s.Interaction.Entities = null;
        await s.Panel.Prepare.PickLinesAsync();
        Assert.Equal(StatusKind.Info, s.Panel.Messages.Kind);

        s.Interaction.Entities = [];
        await s.Panel.Prepare.PickLinesAsync();
        Assert.Equal(StatusKind.Warning, s.Panel.Messages.Kind);
        Assert.Empty(s.Drawing.Walls);
    }

    [Fact]
    public async Task Without_unit_or_with_a_bad_merge_gap_nothing_is_asked()
    {
        Sample s = WithLooseFaces();
        s.Panel.Prepare.MergeGapText = "abc";
        await s.Panel.Prepare.PickLinesAsync();
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);

        Sample noUnit = Sample.Custom(d => d.Unit = null);
        await noUnit.Panel.Prepare.PickLinesAsync();

        Assert.Empty(s.Interaction.Prompts);
        Assert.Empty(noUnit.Interaction.Prompts);
    }

    [Fact]
    public async Task Axes_from_a_selection_survive_generating_a_zone()
    {
        Sample s = WithLooseFaces();
        s.Drawing.AddFace("muros", "a", 0, 3, 5, 3);
        s.Drawing.AddFace("muros", "b", 0, 3.2, 5, 3.2);
        s.Interaction.Entities = ["F1", "F2"];
        await s.Panel.Prepare.PickLinesAsync();
        s.Interaction.Window = new BoundingBox(new Vec2(-1, -1), new Vec2(11, 9));
        await s.Panel.Prepare.PickWindowAsync();

        s.Panel.Prepare.GenerateAxes();

        Assert.Equal(2, s.Drawing.Walls.Count);
    }
}
