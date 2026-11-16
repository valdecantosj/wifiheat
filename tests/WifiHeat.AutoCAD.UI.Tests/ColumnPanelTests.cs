using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;
using Xunit;

namespace WifiHeat.AutoCAD.UI.Tests;

public class ColumnPanelTests
{
    private static Sample WithFigures() => Sample.WithConcrete(d =>
    {
        d.Add("A1", 0, 0, 10, 0, "brick", 0.2);
        d.Add("A2", 0, 8, 10, 8, "brick", 0.2);
        d.AddFigure("H1", "HATCH", 5, 3, 0.3, 0.3);
        d.AddFigure("P1", "LWPOLYLINE", 5, 3, 0.3, 0.3);
        d.AddFace("muros", "a", 0, 0, 5, 0);
        d.AddFace("muros", "b", 0, 0.2, 5, 0.2);
    });

    private static async Task<Sample> PickedFiguresAsync()
    {
        Sample s = WithFigures();
        s.Panel.Walls.IsColumnKind = true;
        s.Interaction.Entities = ["H1", "P1"];
        await s.Panel.Walls.PickInDrawingAsync();
        return s;
    }

    [Fact]
    public void The_assignment_starts_as_wall_with_medium_reinforcement_ready()
    {
        Sample s = WithFigures();
        WallsViewModel walls = s.Panel.Walls;

        Assert.True(walls.IsWallKind);
        Assert.False(walls.IsColumnKind);
        Assert.Equal(["Baja · +2,5 dB", "Media · +5 dB", "Alta · +7,5 dB"], walls.Reinforcements.Select(r => r.Label));
        Assert.Equal(Reinforcement.Medium, walls.BulkReinforcement.Level);
        Assert.Equal("Asignar a los muros seleccionados", walls.AssignHeading);
    }

    [Fact]
    public void Choosing_column_fixes_the_material_and_describes_each_level()
    {
        Sample s = WithFigures();
        WallsViewModel walls = s.Panel.Walls;

        walls.IsColumnKind = true;
        walls.BulkReinforcement = walls.Reinforcements.Single(r => r.Level == Reinforcement.High);

        Assert.False(walls.IsWallKind);
        Assert.Equal("Marcar la selección como columnas", walls.AssignHeading);
        Assert.Equal("Material: Hormigón (fijo para las columnas).", walls.ColumnMaterialText);
        Assert.StartsWith("12 barras Ø20", walls.ReinforcementText);
        Assert.Contains("27,5 dB a 2,4 GHz · 47,5 dB a 5 GHz", walls.ReinforcementText);
        Assert.Contains("convención del proyecto", walls.ReinforcementText);
    }

    [Fact]
    public async Task Picking_for_columns_also_asks_for_hatches_and_shows_its_own_mode()
    {
        Sample s = WithFigures();
        s.Interaction.Entities = [];
        await s.Panel.Walls.PickInDrawingAsync();
        s.Panel.Walls.IsColumnKind = true;
        s.Interaction.Entities = ["H1", "P1"];

        await s.Panel.Walls.PickInDrawingAsync();

        Assert.Equal([false, true], s.Interaction.HatchRequests);
        Assert.Equal(2, s.Panel.Walls.ExtraSelectionCount);
        Assert.Equal("MODO: ELEGIR COLUMNAS", PanelMode.PickColumns.Title);
    }

    [Fact]
    public async Task Assign_marks_the_hatch_and_its_outline_as_one_column()
    {
        Sample s = await PickedFiguresAsync();

        s.Panel.Walls.Assign();

        Wall column = Assert.Single(s.Drawing.Walls.Values, w => w.IsColumn);
        Assert.Equal("concrete", column.MaterialId);
        Assert.Equal(Reinforcement.Medium, column.Reinforcement);
        Assert.Equal(2, s.Drawing.Figures.Count);
        Assert.Equal(StatusKind.Success, s.Panel.Messages.Kind);
        Assert.Equal("1 columna con armadura media", s.Panel.Messages.Current!.Title);
        Assert.Contains("Hormigón + 5 dB. 1 creada(s), 0 que ya existía(n), 1 figura(s) repetida(s)", s.Panel.Messages.Current.Detail);
        Assert.Equal(0, s.Panel.Walls.ExtraSelectionCount);
        Assert.True(s.Panel.Validation.IsValid);
    }

    [Fact]
    public async Task The_table_shows_the_column_apart_from_the_walls()
    {
        Sample s = await PickedFiguresAsync();
        s.Panel.Walls.Assign();

        WallRowViewModel row = s.Panel.Walls.Rows.Single(r => r.IsColumn);

        Assert.True(row.IsSelected);
        Assert.False(row.IsWall);
        Assert.Equal("Columna · armadura media", row.ColumnText);
        Assert.Equal("Hormigón + armadura media: 25 dB a 2,4 GHz · 45 dB a 5 GHz", row.MaterialNote);
        Assert.Equal("", row.ThicknessText);
        Assert.Equal("2 muro(s) · 1 columna(s) · 0 sin material", s.Panel.Walls.CountText);
        Assert.Equal("", s.Row("A1").ColumnText);
    }

    [Fact]
    public async Task Selecting_the_column_row_changes_its_reinforcement()
    {
        Sample s = await PickedFiguresAsync();
        s.Panel.Walls.Assign();
        s.Panel.Walls.BulkReinforcement = s.Panel.Walls.Reinforcements.Single(r => r.Level == Reinforcement.Low);

        s.Panel.Walls.Assign();

        Assert.Equal(Reinforcement.Low, Assert.Single(s.Drawing.Walls.Values, w => w.IsColumn).Reinforcement);
        Assert.Equal("Columna · armadura baja", s.Panel.Walls.Rows.Single(r => r.IsColumn).ColumnText);
        Assert.Contains("0 creada(s), 1 que ya existía(n)", s.Panel.Messages.Current!.Detail);
    }

    [Fact]
    public async Task A_figure_that_is_too_large_is_explained_and_nothing_is_created()
    {
        Sample s = WithFigures();
        s.Drawing.AddFigure("ROOM", "LWPOLYLINE", 0, 0, 4, 3);
        s.Panel.Walls.IsColumnKind = true;
        s.Interaction.Entities = ["ROOM"];
        await s.Panel.Walls.PickInDrawingAsync();

        s.Panel.Walls.Assign();

        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
        Assert.Equal("No se generaron columnas", s.Panel.Messages.Current!.Title);
        Assert.Contains("No parece una columna", s.Panel.Messages.Current.Detail);
        Assert.DoesNotContain(s.Drawing.Walls.Values, w => w.IsColumn);
        Assert.Equal(1, s.Panel.Walls.ExtraSelectionCount);
    }

    [Fact]
    public async Task Generating_axes_reports_small_closed_figures_with_an_action()
    {
        Sample s = WithFigures();
        s.Interaction.Window = new BoundingBox(new Vec2(-1, -1), new Vec2(11, 9));
        await s.Panel.Prepare.PickWindowAsync();

        s.Panel.Prepare.GenerateAxes();

        Assert.True(s.Panel.Prepare.HasSmallFigures);
        Assert.Equal("Se detectó 1 figura cerrada pequeña (posible columna).", s.Panel.Prepare.SmallFiguresText);
        Assert.EndsWith("Se detectó 1 figura cerrada pequeña (posible columna).", s.Panel.Messages.Current!.Detail);
        Assert.Equal(["Marcar como columnas"], s.Panel.Messages.Current.Actions.Select(a => a.Label));
    }

    [Fact]
    public async Task Marking_the_small_figures_creates_the_columns_and_clears_the_notice()
    {
        Sample s = WithFigures();
        s.Interaction.Window = new BoundingBox(new Vec2(-1, -1), new Vec2(11, 9));
        await s.Panel.Prepare.PickWindowAsync();
        s.Panel.Prepare.GenerateAxes();

        s.Panel.Messages.Current!.Actions[0].Command.Execute(null);

        Assert.Single(s.Drawing.Walls.Values, w => w.IsColumn);
        Assert.False(s.Panel.Prepare.HasSmallFigures);
        Assert.Equal("1 columna con armadura media", s.Panel.Messages.Current!.Title);

        s.Panel.Prepare.GenerateAxes();
        Assert.False(s.Panel.Prepare.HasSmallFigures);
        Assert.Empty(s.Panel.Messages.Current!.Actions);
    }

    [Fact]
    public async Task Showing_the_small_figures_selects_them_in_the_drawing()
    {
        Sample s = WithFigures();
        s.Interaction.Window = new BoundingBox(new Vec2(-1, -1), new Vec2(11, 9));
        await s.Panel.Prepare.PickWindowAsync();
        s.Panel.Prepare.GenerateAxes();

        s.Panel.Prepare.ShowSmallFigures();

        Assert.Equal(["H1"], s.Interaction.Shown[^1].Handles);
    }

    [Fact]
    public void Without_concrete_in_the_catalog_the_section_says_so()
    {
        Sample s = Sample.Room();

        Assert.Null(s.Panel.Walls.ColumnMaterial);
        Assert.Equal("El catálogo no tiene el material de las columnas.", s.Panel.Walls.ColumnMaterialText);
    }
}
