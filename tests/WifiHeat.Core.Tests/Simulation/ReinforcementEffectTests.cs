using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class ReinforcementEffectTests
{
    private static readonly MaterialCatalog Catalog = new(
    [
        new Material("concrete", "Hormigón", 22.8, 44.8),
        new Material("brick", "Ladrillo", 9, 17.5),
        new Material("lead", "Plomo", 60, 80),
    ]);

    private static readonly RadioSettings Settings = new() { TxPowerDbm = 20, FrequencyMHzOverride = 2400 };

    private static readonly GridSpec Spec = new(new Vec2(0, -1), 0.5, 20, 4);

    private static readonly Polygon2 Area = Polygon2.Rectangle(new BoundingBox(new Vec2(0, -1), new Vec2(10, 1)));

    private static Segment2 S(double ax, double ay, double bx, double by) => new(new Vec2(ax, ay), new Vec2(bx, by));

    private static Wall Column(string id, double x, Reinforcement level = Reinforcement.Medium) =>
        Wall.Column(id, Polygon2.Rectangle(new BoundingBox(new Vec2(x, 0), new Vec2(x + 0.3, 0.3))), "concrete", level);

    private static Wall WallOf(string id, string material, Segment2 segment) => new(id, [segment], material, 0.15);

    private static ReinforcementEffectMap? Effect(IReadOnlyList<Wall> walls, Vec2? ap = null) =>
        ReinforcementEffect.Calculate(new SimulationScene(walls, Catalog), new AccessPoint(ap ?? new Vec2(0, 0.15)), Settings, Spec, Area);

    private static int ColumnOf(double x) => (int)Math.Floor(x / 0.5);

    private const int RowBehind = 2;

    [Fact]
    public void Without_reinforced_columns_there_is_nothing_to_show()
    {
        Assert.Null(Effect([WallOf("W", "brick", S(5, -3, 5, 3))]));
        Assert.Null(Effect([]));
    }

    [Theory]
    [InlineData(Reinforcement.Low, 2.5)]
    [InlineData(Reinforcement.Medium, 5)]
    [InlineData(Reinforcement.High, 7.5)]
    public void A_column_adds_its_level_behind_it_and_nothing_in_front(Reinforcement level, double expectedDb)
    {
        ReinforcementEffectMap map = Effect([Column("C", 5, level)])!;

        Assert.Equal(expectedDb, map.Grid[ColumnOf(6.25), RowBehind], 9);
        Assert.Equal(expectedDb, map.MaxDb, 9);
        Assert.True(double.IsNaN(map.Grid[ColumnOf(1.25), RowBehind]));
        Assert.True(map.HasEffect);
    }

    [Fact]
    public void Cells_off_the_shadow_have_no_value()
    {
        ReinforcementEffectMap map = Effect([Column("C", 5, Reinforcement.High)])!;

        Assert.True(double.IsNaN(map.Grid[ColumnOf(8.25), 0]));
        Assert.True(double.IsNaN(map.Grid[ColumnOf(8.25), 3]));
        Assert.True(map.AffectedCells > 0);
        Assert.True(map.AffectedCells < Spec.CellCount / 2);
    }

    [Fact]
    public void A_ray_through_two_columns_adds_both_levels()
    {
        ReinforcementEffectMap map = Effect([Column("A", 3, Reinforcement.Medium), Column("B", 6, Reinforcement.High)])!;

        Assert.Equal(5, map.Grid[ColumnOf(4.25), RowBehind], 9);
        Assert.Equal(12.5, map.Grid[ColumnOf(8.25), RowBehind], 9);
        Assert.Equal(12.5, map.MaxDb, 9);
    }

    [Fact]
    public void Two_high_columns_add_fifteen_db()
    {
        ReinforcementEffectMap map = Effect([Column("A", 3, Reinforcement.High), Column("B", 6, Reinforcement.High)])!;

        Assert.Equal(15, map.Grid[ColumnOf(8.25), RowBehind], 9);
    }

    [Fact]
    public void A_column_touching_a_weaker_wall_still_adds_the_reinforcement()
    {
        ReinforcementEffectMap map = Effect([Column("C", 5), WallOf("W", "brick", S(5.3, -3, 5.3, 3))])!;

        Assert.Equal(5, map.Grid[ColumnOf(6.25), RowBehind], 9);
    }

    [Fact]
    public void A_column_touching_a_stronger_wall_adds_nothing_there()
    {
        ReinforcementEffectMap map = Effect([Column("C", 5), WallOf("W", "lead", S(5.3, -3, 5.3, 3))])!;

        for (int column = ColumnOf(5.75); column < Spec.Columns; column++)
        {
            Assert.True(double.IsNaN(map.Grid[column, RowBehind]));
        }
    }

    [Fact]
    public void A_wall_between_the_column_and_the_cell_does_not_change_the_added_loss()
    {
        ReinforcementEffectMap map = Effect([Column("C", 5), WallOf("W", "brick", S(8, -3, 8, 3))])!;

        Assert.Equal(5, map.Grid[ColumnOf(9.25), RowBehind], 9);
    }

    [Fact]
    public void The_cell_of_the_access_point_has_no_value()
    {
        ReinforcementEffectMap map = Effect([Column("C", 5)], new Vec2(1.25, 0.25))!;

        Assert.True(double.IsNaN(map.Grid[ColumnOf(1.25), RowBehind]));
        Assert.Equal(5, map.Grid[ColumnOf(6.25), RowBehind], 9);
    }

    [Fact]
    public void Cells_outside_the_area_have_no_value()
    {
        var half = Polygon2.Rectangle(new BoundingBox(new Vec2(0, -1), new Vec2(5, 1)));
        ReinforcementEffectMap map = ReinforcementEffect.Calculate(
            new SimulationScene([Column("C", 3)], Catalog), new AccessPoint(new Vec2(0, 0.15)), Settings, Spec, half)!;

        Assert.True(double.IsNaN(map.Grid[ColumnOf(8.25), RowBehind]));
        Assert.Equal(5, map.Grid[ColumnOf(4.25), RowBehind], 9);
    }

    [Fact]
    public void The_difference_equals_the_two_calculations()
    {
        var walls = new[] { Column("C", 5, Reinforcement.High) };
        var scene = new SimulationScene(walls, Catalog);
        var ap = new AccessPoint(new Vec2(0, 0.15));
        var point = new Vec2(6.25, 0.25);

        double normal = PointCalculator.ReceivedPowerDbm(scene, ap, Settings, point);
        double plain = PointCalculator.ReceivedPowerDbm(new SimulationScene(walls, Catalog, includeReinforcement: false), ap, Settings, point);

        Assert.Equal(plain - normal, Effect(walls)!.Grid[ColumnOf(6.25), RowBehind], 9);
    }

    [Theory]
    [InlineData(0.5, 0.2)]
    [InlineData(5, 0.2)]
    [InlineData(0.2, 0.2)]
    [InlineData(0.1, 0.1)]
    public void The_resolution_is_at_most_twenty_centimetres(double session, double expected)
    {
        Assert.Equal(expected, ReinforcementEffect.CellSizeFor(session), 9);
    }

    [Theory]
    [InlineData(7.5, 7.5)]
    [InlineData(2.5, 2.5)]
    [InlineData(5, 5)]
    [InlineData(12.5, 12.5)]
    [InlineData(3.1, 5)]
    [InlineData(1.3, 2.5)]
    public void The_scale_tops_out_at_a_multiple_of_two_and_a_half(double max, double top)
    {
        Assert.Equal(top, new EffectScale(max).TopDb, 9);
    }

    [Fact]
    public void The_scale_has_five_levels_from_light_yellow_to_dark_red()
    {
        var scale = new EffectScale(7.5);
        IReadOnlyList<SignalLevel> levels = scale.Levels();

        Assert.Equal(EffectScale.Steps, levels.Count);
        Assert.Equal(1.5, scale.StepDb, 9);
        Assert.Equal("0 a 1,5 dB", levels[^1].Label);
        Assert.Equal("6 a 7,5 dB", levels[0].Label);
        Assert.Equal(new Rgb(255, 245, 157), scale.Color(0));
        Assert.Equal(new Rgb(120, 10, 15), scale.Color(7.5));
        Assert.Equal((levels[0].R, levels[0].G, levels[0].B), (scale.Color(6.75).R, scale.Color(6.75).G, scale.Color(6.75).B));
    }

    [Fact]
    public void The_scale_colour_gets_darker_with_the_value()
    {
        var scale = new EffectScale(7.5);
        int previous = int.MaxValue;
        for (double db = 0; db <= 7.5; db += 0.5)
        {
            Rgb c = scale.Color(db);
            int brightness = c.R + c.G + c.B;
            Assert.True(brightness <= previous);
            previous = brightness;
        }
    }

    [Fact]
    public void The_scale_rejects_a_maximum_of_zero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectScale(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EffectScale(double.NaN));
    }

    [Fact]
    public void The_image_paints_only_the_shadow()
    {
        ReinforcementEffectMap map = Effect([Column("C", 5, Reinforcement.High)])!;
        HeatmapImage image = HeatmapRasterizer.RenderEffect(map.Grid, new EffectScale(map.MaxDb));

        (byte R, byte G, byte B, byte A) shadow = PixelAt(image, 6.25, 0.25);
        (byte R, byte G, byte B, byte A) front = PixelAt(image, 2.25, 0.25);
        (byte R, byte G, byte B, byte A) above = PixelAt(image, 8.25, 0.9);

        Assert.Equal(255, shadow.A);
        Assert.Equal(new Rgb(120, 10, 15), new Rgb(shadow.R, shadow.G, shadow.B));
        Assert.Equal(0, front.A);
        Assert.Equal(0, above.A);
    }

    [Fact]
    public void The_image_is_transparent_outside_the_area()
    {
        var half = Polygon2.Rectangle(new BoundingBox(new Vec2(0, -1), new Vec2(5, 1)));
        ReinforcementEffectMap map = ReinforcementEffect.Calculate(
            new SimulationScene([Column("C", 3)], Catalog), new AccessPoint(new Vec2(0, 0.15)), Settings, Spec, half)!;
        HeatmapImage image = HeatmapRasterizer.RenderEffect(map.Grid, new EffectScale(map.MaxDb));

        Assert.True(image.WidthM <= 5 + image.PixelSizeM);
        Assert.Equal(255, PixelAt(image, 4.25, 0.25).A);
    }

    [Fact]
    public void The_legend_has_its_own_title_and_levels()
    {
        Legend legend = Legend.ForReinforcementEffect(Settings, new EffectScale(7.5));

        Assert.Equal("Pérdida que agrega la armadura (dB)", legend.Title);
        Assert.Equal(EffectScale.Steps, legend.Levels.Count);
        Assert.Contains(legend.Details, d => d.Contains("2,4 GHz"));
    }

    private static (byte R, byte G, byte B, byte A) PixelAt(HeatmapImage image, double x, double y)
    {
        int px = (int)((x - image.Origin.X) / image.PixelSizeM);
        int py = image.Height - 1 - (int)((y - image.Origin.Y) / image.PixelSizeM);
        return image.Pixel(Math.Clamp(px, 0, image.Width - 1), Math.Clamp(py, 0, image.Height - 1));
    }
}

public class ReinforcementEffectSessionTests
{
    private static readonly MaterialCatalog Catalog = new([new Material("concrete", "Hormigón", 22.8, 44.8), new Material("brick", "Ladrillo", 9, 17.5)]);

    private static Wall Column(Reinforcement level) =>
        Wall.Column("C", Polygon2.Rectangle(new BoundingBox(new Vec2(5, 4), new Vec2(5.3, 4.3))), "concrete", level);

    private static readonly Wall Perimeter = new("P", [
        new Segment2(new Vec2(0, 0), new Vec2(10, 0)), new Segment2(new Vec2(10, 0), new Vec2(10, 8)),
        new Segment2(new Vec2(10, 8), new Vec2(0, 8)), new Segment2(new Vec2(0, 8), new Vec2(0, 0))], "brick", 0.15);

    private static SimulationSession Session(double cellSizeM, params Wall[] extra)
    {
        var session = new SimulationSession(Catalog);
        session.SetResolution(cellSizeM);
        session.LoadWalls([Perimeter, .. extra]);
        session.PlaceAccessPoint(new Vec2(1, 4.15));
        return session;
    }

    [Fact]
    public void A_coarse_session_resolution_is_lowered_to_twenty_centimetres_without_changing_the_session()
    {
        SimulationSession session = Session(0.5, Column(Reinforcement.High));
        RssiGrid coverage = session.Calculate().Value!;

        ReinforcementEffectResult result = session.CalculateReinforcementEffect(null).Value!;

        Assert.Equal(0.2, result.CellSizeM, 9);
        Assert.True(result.ResolutionLowered);
        Assert.Equal(0.5, session.CellSizeM);
        Assert.Same(coverage, session.LastGrid);
        Assert.Equal(1, result.ReinforcedColumns);
        Assert.Equal(7.5, result.Map!.MaxDb, 9);
    }

    [Fact]
    public void A_fine_session_resolution_is_kept()
    {
        SimulationSession session = Session(0.1, Column(Reinforcement.Low));

        ReinforcementEffectResult result = session.CalculateReinforcementEffect(null).Value!;

        Assert.Equal(0.1, result.CellSizeM, 9);
        Assert.False(result.ResolutionLowered);
        Assert.Equal(2.5, result.Map!.MaxDb, 9);
    }

    [Fact]
    public void Without_reinforced_columns_the_map_is_null_and_it_is_not_an_error()
    {
        SimulationSession session = Session(0.5);

        OperationResult<ReinforcementEffectResult> result = session.CalculateReinforcementEffect(null);

        Assert.True(result.Succeeded);
        Assert.Null(result.Value!.Map);
        Assert.Equal(0, result.Value.ReinforcedColumns);
    }

    [Fact]
    public void It_needs_an_access_point_when_there_are_reinforced_columns()
    {
        var session = new SimulationSession(Catalog);
        session.LoadWalls([Perimeter, Column(Reinforcement.Medium)]);

        OperationResult<ReinforcementEffectResult> result = session.CalculateReinforcementEffect(null);

        Assert.False(result.Succeeded);
        Assert.Contains("AP", result.Errors[0]);
    }

    [Fact]
    public void It_needs_a_validated_plan()
    {
        var session = new SimulationSession(Catalog);

        Assert.False(session.CalculateReinforcementEffect(null).Succeeded);
    }
}
