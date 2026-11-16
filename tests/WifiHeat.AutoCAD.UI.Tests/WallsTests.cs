using WifiHeat.AutoCAD.UI.ViewModels;
using Xunit;

namespace WifiHeat.AutoCAD.UI.Tests;

public class WallsTests
{
    private static MaterialOption Material(Sample s, string id) => s.Panel.Materials.Single(m => m.Id == id);

    [Fact]
    public void Rows_show_handle_layer_length_material_and_thickness()
    {
        Sample s = Sample.Room();

        WallRowViewModel row = s.Row("A2");

        Assert.Equal("WIFIHEAT_MUROS", row.Layer);
        Assert.Equal("8,00", row.LengthText);
        Assert.Equal("brick", row.Material!.Id);
        Assert.Equal("0,2", row.ThicknessText);
        Assert.False(row.MissingMaterial);
        Assert.Equal("5 muro(s) · 0 sin material", s.Panel.Walls.CountText);
    }

    [Fact]
    public void Empty_drawing_explains_how_to_get_walls()
    {
        Sample s = Sample.Empty();

        Assert.False(s.Panel.Walls.HasWalls);
        Assert.StartsWith("No hay muros.", s.Panel.Walls.EmptyText);
    }

    [Fact]
    public void Materials_come_from_the_catalog_sorted_by_name()
    {
        Sample s = Sample.Room();

        Assert.Equal(["Ladrillo", "Yeso"], s.Panel.Walls.Materials.Select(m => m.Name));
    }

    [Fact]
    public void Changing_a_row_material_writes_it_and_keeps_the_thickness()
    {
        Sample s = Sample.Room(material: null);

        s.Row("B1").Material = Material(s, "gypsum");

        Assert.Equal("gypsum", s.Drawing.Walls["B1"].MaterialId);
        Assert.Equal(0.1, s.Drawing.Walls["B1"].ThicknessM);
        Assert.Equal(4, s.Panel.Validation.ErrorCount);
        Assert.Equal(StatusKind.Success, s.Panel.Messages.Kind);
    }

    [Fact]
    public void Editing_a_row_keeps_the_same_row_instances()
    {
        Sample s = Sample.Room();
        WallRowViewModel before = s.Row("A1");

        before.Material = Material(s, "gypsum");

        Assert.Same(before, s.Row("A1"));
        Assert.Same(before, s.Panel.Walls.VisibleRows[0]);
    }

    [Fact]
    public void Editing_a_row_thickness_writes_it_with_a_decimal_comma()
    {
        Sample s = Sample.Room();

        s.Row("A1").ThicknessText = "0,35";

        Assert.Equal(0.35, s.Drawing.Walls["A1"].ThicknessM);
        Assert.Equal("brick", s.Drawing.Walls["A1"].MaterialId);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-0,2")]
    [InlineData("")]
    public void Invalid_row_thickness_is_rejected_and_restored(string text)
    {
        Sample s = Sample.Room();

        s.Row("A1").ThicknessText = text;

        Assert.Equal(0.2, s.Drawing.Walls["A1"].ThicknessM);
        Assert.Equal("0,2", s.Row("A1").ThicknessText);
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
    }

    [Fact]
    public void Thickness_of_a_wall_without_material_asks_for_the_material_first()
    {
        Sample s = Sample.Room(material: null);

        s.Row("A1").ThicknessText = "0,3";

        Assert.Equal(0.2, s.Drawing.Walls["A1"].ThicknessM);
        Assert.Equal("0,2", s.Row("A1").ThicknessText);
        Assert.Equal(StatusKind.Warning, s.Panel.Messages.Kind);
    }

    [Fact]
    public void Material_not_in_the_catalog_is_flagged()
    {
        Sample s = Sample.Custom(d => d.Add("X1", 0, 0, 5, 0, "marble", 0.2));

        WallRowViewModel row = s.Row("X1");

        Assert.Null(row.Material);
        Assert.True(row.UnknownMaterial);
        Assert.False(row.MissingMaterial);
        Assert.Contains("marble", row.MaterialNote);
        Assert.Equal("¿marble?", row.MaterialPlaceholder);
    }

    [Fact]
    public void Filter_without_material_shows_only_those_walls()
    {
        Sample s = Sample.Room();
        s.Drawing.Add("C9", 2, 2, 3, 2);
        s.Panel.Refresh();

        s.Panel.Walls.ShowMissing = true;

        Assert.Equal(["C9"], s.Panel.Walls.VisibleRows.Select(r => r.Handle));
        Assert.Equal(WallFilter.MissingMaterial, s.Panel.Walls.Filter);
        Assert.False(s.Panel.Walls.ShowAll);
    }

    [Fact]
    public void A_wall_leaves_the_missing_filter_when_it_gets_material()
    {
        Sample s = Sample.Room();
        s.Drawing.Add("C9", 2, 2, 3, 2);
        s.Panel.Refresh();
        s.Panel.Walls.ShowMissing = true;

        s.Row("C9").Material = Material(s, "brick");

        Assert.Empty(s.Panel.Walls.VisibleRows);
        Assert.Equal("Todos los muros tienen material.", s.Panel.Walls.EmptyText);
    }

    [Fact]
    public void Filter_selected_follows_the_checkboxes()
    {
        Sample s = Sample.Room();
        s.Panel.Walls.ShowSelected = true;
        Assert.Empty(s.Panel.Walls.VisibleRows);
        Assert.Equal("No hay muros seleccionados.", s.Panel.Walls.EmptyText);

        s.Row("A2").IsSelected = true;
        s.Row("A4").IsSelected = true;

        Assert.Equal(["A2", "A4"], s.Panel.Walls.VisibleRows.Select(r => r.Handle));
        Assert.Equal("2 muro(s) seleccionado(s)", s.Panel.Walls.SelectionText);
    }

    [Fact]
    public void Select_all_marks_only_the_visible_rows()
    {
        Sample s = Sample.Room();
        s.Drawing.Add("C9", 2, 2, 3, 2);
        s.Panel.Refresh();
        s.Panel.Walls.ShowMissing = true;

        s.Panel.Walls.SelectAllVisible();

        Assert.Equal(1, s.Panel.Walls.SelectedCount);
        Assert.True(s.Row("C9").IsSelected);
        s.Panel.Walls.ClearSelection();
        Assert.False(s.Panel.Walls.HasSelection);
    }

    [Fact]
    public async Task Picking_in_the_drawing_selects_rows_and_counts_new_entities()
    {
        Sample s = Sample.Room();
        s.Row("A3").IsSelected = true;
        s.Interaction.Entities = ["A1", "b1", "2F0"];

        await s.Panel.Walls.PickInDrawingAsync();

        Assert.True(s.Row("A1").IsSelected);
        Assert.True(s.Row("B1").IsSelected);
        Assert.False(s.Row("A3").IsSelected);
        Assert.Equal(2, s.Panel.Walls.SelectedCount);
        Assert.Equal(1, s.Panel.Walls.ExtraSelectionCount);
        Assert.Equal("2 muro(s) y 1 entidad(es) nueva(s) seleccionados", s.Panel.Walls.SelectionText);
    }

    [Fact]
    public async Task Cancelling_the_pick_keeps_the_selection()
    {
        Sample s = Sample.Room();
        s.Row("A3").IsSelected = true;
        s.Interaction.Entities = null;

        await s.Panel.Walls.PickInDrawingAsync();

        Assert.True(s.Row("A3").IsSelected);
        Assert.Equal(StatusKind.Info, s.Panel.Messages.Kind);
    }

    [Fact]
    public void Assign_writes_material_and_thickness_to_the_selected_walls()
    {
        Sample s = Sample.Room(material: null);
        s.Row("A1").IsSelected = true;
        s.Row("A2").IsSelected = true;
        s.Panel.Walls.BulkMaterial = Material(s, "brick");
        s.Panel.Walls.BulkThicknessText = "0,3";

        s.Panel.Walls.Assign();

        Assert.Equal("brick", s.Drawing.Walls["A1"].MaterialId);
        Assert.Equal(0.3, s.Drawing.Walls["A2"].ThicknessM);
        Assert.Null(s.Drawing.Walls["A3"].MaterialId);
        Assert.Equal("2 muro(s) con Ladrillo", s.Panel.Messages.Current!.Title);
        Assert.Equal("Espesor 0,3 m.", s.Panel.Messages.Current.Detail);
        Assert.True(s.Row("A1").IsSelected);
    }

    [Fact]
    public void Assign_with_empty_thickness_keeps_each_wall_thickness()
    {
        Sample s = Sample.Room(material: null);
        s.Panel.Walls.SelectAllVisible();
        s.Panel.Walls.BulkMaterial = Material(s, "gypsum");

        s.Panel.Walls.Assign();

        Assert.Equal(0.2, s.Drawing.Walls["A1"].ThicknessM);
        Assert.Equal(0.1, s.Drawing.Walls["B1"].ThicknessM);
        Assert.True(s.Panel.Validation.IsValid);
        Assert.Equal("Espesor conservado.", s.Panel.Messages.Current!.Detail);
    }

    [Fact]
    public async Task Assign_turns_picked_entities_into_walls_and_keeps_them_selected()
    {
        Sample s = Sample.Empty();
        s.Drawing.Add("L1", 0, 0, 4, 0);
        s.Drawing.Walls.Remove("L1", out var line);
        s.Interaction.Entities = ["L1"];
        await s.Panel.Walls.PickInDrawingAsync();
        s.Drawing.Walls["L1"] = line!;
        s.Panel.Walls.BulkMaterial = Material(s, "brick");

        s.Panel.Walls.Assign();

        Assert.Equal(0, s.Panel.Walls.ExtraSelectionCount);
        Assert.True(s.Row("L1").IsSelected);
        Assert.Equal("brick", s.Row("L1").Material!.Id);
    }

    [Fact]
    public void Assign_without_selection_or_material_explains_what_is_missing()
    {
        Sample s = Sample.Room(material: null);

        s.Panel.Walls.Assign();
        Assert.Contains("No hay muros seleccionados", s.Panel.Messages.Text);

        s.Row("A1").IsSelected = true;
        s.Panel.Walls.Assign();
        Assert.Equal("Elegir el material a asignar.", s.Panel.Messages.Current!.Detail);
        Assert.Null(s.Drawing.Walls["A1"].MaterialId);
    }

    [Fact]
    public void Assign_with_invalid_thickness_changes_nothing()
    {
        Sample s = Sample.Room(material: null);
        s.Row("A1").IsSelected = true;
        s.Panel.Walls.BulkMaterial = Material(s, "brick");
        s.Panel.Walls.BulkThicknessText = "grueso";

        s.Panel.Walls.Assign();

        Assert.Null(s.Drawing.Walls["A1"].MaterialId);
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
    }

    [Fact]
    public void Chosen_material_shows_its_loss_in_both_bands()
    {
        Sample s = Sample.Room();
        Assert.StartsWith("Elegir un material", s.Panel.Walls.MaterialLossText);

        s.Panel.Walls.BulkMaterial = Material(s, "brick");

        Assert.Equal("Ladrillo: 8 dB a 2,4 GHz · 12 dB a 5 GHz", s.Panel.Walls.MaterialLossText);
    }

    [Fact]
    public void Show_selection_zooms_to_the_selected_walls()
    {
        Sample s = Sample.Room();
        s.Row("A2").IsSelected = true;

        s.Panel.Walls.ShowSelection();
        s.Panel.Walls.ShowRow(s.Row("B1"));

        Assert.Equal(["A2"], s.Interaction.Shown[0].Handles);
        Assert.Equal(["B1"], s.Interaction.Shown[1].Handles);
        Assert.True(s.Interaction.Shown[1].Zoom);
    }

    [Fact]
    public void Selection_survives_a_refresh_and_erased_walls_disappear()
    {
        Sample s = Sample.Room();
        s.Row("A2").IsSelected = true;
        s.Row("B1").IsSelected = true;
        s.Drawing.Walls.Remove("B1");

        s.Panel.Refresh();

        Assert.True(s.Row("A2").IsSelected);
        Assert.Equal(4, s.Panel.Walls.Rows.Count);
        Assert.Equal(1, s.Panel.Walls.SelectedCount);
    }

    [Fact]
    public void A_busy_drawing_reverts_the_edit_and_explains_why()
    {
        Sample s = Sample.Room();
        s.Interaction.BusyReason = "AutoCAD está ejecutando LINE.";

        s.Row("A1").Material = Material(s, "gypsum");

        Assert.Equal("brick", s.Drawing.Walls["A1"].MaterialId);
        Assert.Equal("brick", s.Row("A1").Material!.Id);
        Assert.Equal("AutoCAD está ejecutando LINE.", s.Panel.Messages.Current!.Detail);
        Assert.Equal(StatusKind.Warning, s.Panel.Messages.Kind);
    }
}
