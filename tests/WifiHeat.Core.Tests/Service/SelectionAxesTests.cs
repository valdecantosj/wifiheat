using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Service;

public class SelectionAxesTests
{
    private static FakeDrawing FacesInDifferentLayers()
    {
        var d = new FakeDrawing();
        d.AddLine("muros", "F1", 5, 0, 5, 8);
        d.AddLine("0", "F2", 5.2, 0, 5.2, 8);
        d.AddLine("revoque", "G1", 0, 3, 5, 3);
        d.AddLine("0", "G2", 0, 3.15, 5, 3.15);
        d.AddLine("0", "LOOSE", 8, 0, 8, 8);
        return d;
    }

    [Fact]
    public void Pairs_inside_the_selection_become_axes_whatever_their_layer()
    {
        FakeDrawing d = FacesInDifferentLayers();

        OperationResult<AxisGeneration> result = d.GenerateAxesFromSelection(["F1", "F2", "G1", "G2", "LOOSE"]);

        Assert.True(result.Succeeded);
        AxisGeneration g = result.Value!;
        Assert.Equal((5, 2, 2, 0, 0), (g.SegmentCount, g.AxisCount, g.Created, g.Kept, g.Removed));
        Assert.Equal(0.15, g.MinThicknessM, 9);
        Assert.Equal(0.2, g.MaxThicknessM, 9);
        Assert.Equal(["LOOSE"], g.Unpaired);
        Assert.Equal(2, d.Walls.Count);
        Assert.All(d.Walls.Values, w => Assert.Null(w.MaterialId));
        Assert.Equal(5, d.Lines.Count);
        Assert.Equal("revoque", d.Lines["G1"].Layer);
    }

    [Fact]
    public void Only_the_chosen_entities_are_used()
    {
        FakeDrawing d = FacesInDifferentLayers();

        AxisGeneration g = d.GenerateAxesFromSelection(["F1", "F2"]).Value!;

        Assert.Equal(1, g.AxisCount);
        Wall axis = Assert.Single(d.Walls.Values);
        Assert.Equal(5.1, axis.Segments[0].A.X, 9);
        Assert.Equal(0.2, axis.ThicknessM!.Value, 9);
    }

    [Fact]
    public void A_selection_without_pairs_gives_a_clear_message_and_changes_nothing()
    {
        FakeDrawing d = FacesInDifferentLayers();

        OperationResult<AxisGeneration> result = d.GenerateAxesFromSelection(["F1", "LOOSE"]);

        Assert.True(result.Succeeded);
        Assert.Equal(0, result.Value!.AxisCount);
        Assert.Equal(["F1", "LOOSE"], result.Value.Unpaired);
        Assert.Contains("ningún par de caras paralelas entre las líneas elegidas", Assert.Single(result.Warnings));
        Assert.Empty(d.Walls);
    }

    [Fact]
    public void Repeating_the_selection_keeps_the_axes_and_their_material()
    {
        FakeDrawing d = FacesInDifferentLayers();
        d.GenerateAxesFromSelection(["F1", "F2"]);
        d.AssignMaterial([.. d.Walls.Keys], "brick", null);

        AxisGeneration again = d.GenerateAxesFromSelection(["F1", "F2", "G1", "G2"]).Value!;

        Assert.Equal((2, 1, 1), (again.AxisCount, again.Created, again.Kept));
        Assert.Equal(2, d.Walls.Count);
        Assert.Single(d.Walls.Values, w => w.MaterialId == "brick");
    }

    [Fact]
    public void Generating_from_a_zone_later_does_not_erase_the_axes_made_from_a_selection()
    {
        FakeDrawing d = FacesInDifferentLayers();
        d.GenerateAxesFromSelection(["G1", "G2"]);
        d.AddFace("muros", "a", 5, 0, 5, 8);
        d.AddFace("muros", "b", 5.2, 0, 5.2, 8);

        d.GenerateAxes(new BoundingBox(new Vec2(-1, -1), new Vec2(11, 9)), ["muros"]);

        Assert.Equal(2, d.Walls.Count);
    }

    [Fact]
    public void Walls_and_small_closed_figures_in_the_selection_are_not_faces()
    {
        var d = new FakeDrawing(FakeDrawing.CatalogWithConcrete);
        d.Add("W1", 5, 0, 5, 8, "brick", 0.15);
        d.AddLine("0", "F2", 5.2, 0, 5.2, 8);
        d.AddClosedPolyline("0", "COL", 2, 2, 0.3, 0.3);
        d.AddFigure("COL", "LWPOLYLINE", 2, 2, 0.3, 0.3);

        OperationResult<AxisGeneration> result = d.GenerateAxesFromSelection(["W1", "F2", "COL"]);

        Assert.Equal(0, result.Value!.AxisCount);
        Assert.Equal(1, result.Value.SegmentCount);
        Assert.Equal(["COL"], result.Value.SmallClosedFigures);
        Assert.Single(d.Walls);
    }

    [Fact]
    public void An_empty_or_useless_selection_and_a_missing_unit_are_errors_not_exceptions()
    {
        FakeDrawing d = FacesInDifferentLayers();
        d.Add("W1", 9, 0, 9, 8, "brick", 0.15);

        Assert.Contains("No se eligió ninguna línea", Assert.Single(d.GenerateAxesFromSelection([]).Errors));
        Assert.Contains("caras de muro", Assert.Single(d.GenerateAxesFromSelection(["W1", "NOPE"]).Errors));
        Assert.False(d.GenerateAxesFromSelection(["F1", "F2"], mergeGapM: 5).Succeeded);
        d.Unit = null;
        Assert.Contains("WIFISETUP", Assert.Single(d.GenerateAxesFromSelection(["F1", "F2"]).Errors));
        Assert.Single(d.Walls);
    }
}
