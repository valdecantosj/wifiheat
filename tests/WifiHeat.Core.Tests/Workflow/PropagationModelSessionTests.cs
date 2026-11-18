using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Tests.Data;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Workflow;

public class PropagationModelSessionTests
{
    private static readonly Polygon2 Area = Polygon2.Rectangle(new BoundingBox(new Vec2(-4, -2), new Vec2(7, 2)));

    private static SimulationSession SessionOf(string sceneName = "columna-sola")
    {
        ReferenceScene reference = ReferenceData.Scene(sceneName);
        var session = new SimulationSession(ReferenceData.Catalog);
        session.LoadWalls(reference.Walls);
        session.PlaceAccessPoint(reference.Ap);
        session.Configure(reference.Settings, 0.5);
        session.SetCoverageArea(Area);
        return session;
    }

    [Fact]
    public void Straight_line_is_the_default_model()
    {
        var session = new SimulationSession(ReferenceData.Catalog);

        Assert.Equal(PropagationModel.StraightLine, session.Model);
        Assert.Equal(3, session.MaxDiffractions);
        Assert.Equal(PropagationOptions.Default, session.PropagationOptions);
    }

    [Fact]
    public void Changing_the_model_invalidates_the_last_result()
    {
        SimulationSession session = SessionOf();
        Assert.True(session.Calculate().Succeeded);

        OperationResult<PropagationModel> result = session.SetModel(PropagationModel.Diffraction);

        Assert.True(result.Succeeded);
        Assert.Equal(PropagationModel.Diffraction, session.Model);
        Assert.Null(session.LastGrid);
        Assert.Null(session.Heatmap());
    }

    [Fact]
    public void Setting_the_same_model_keeps_the_last_result()
    {
        SimulationSession session = SessionOf();
        session.SetModel(PropagationModel.Diffraction);
        Assert.True(session.Calculate().Succeeded);

        session.SetModel(PropagationModel.Diffraction);
        session.SetModel(PropagationModel.Diffraction, 3);

        Assert.NotNull(session.LastGrid);
    }

    [Fact]
    public void The_limit_of_diffractions_invalidates_only_when_a_model_uses_it()
    {
        SimulationSession session = SessionOf();
        Assert.True(session.Calculate().Succeeded);

        session.SetModel(PropagationModel.StraightLine, 2);
        Assert.NotNull(session.LastGrid);
        Assert.Equal(2, session.MaxDiffractions);

        session.SetModel(PropagationModel.Diffraction);
        Assert.True(session.Calculate().Succeeded);
        session.SetModel(PropagationModel.Diffraction, 1);

        Assert.Null(session.LastGrid);
        Assert.Equal(1, session.MaxDiffractions);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(-1)]
    public void The_limit_of_diffractions_goes_from_one_to_four(int value)
    {
        SimulationSession session = SessionOf();

        OperationResult<PropagationModel> result = session.SetModel(PropagationModel.Diffraction, value);

        Assert.False(result.Succeeded);
        Assert.Contains("1 a 4", result.Errors[0]);
        Assert.Equal(PropagationModel.StraightLine, session.Model);
        Assert.Equal(3, session.MaxDiffractions);
    }

    [Fact]
    public void The_model_with_reflections_is_offered_and_calculates()
    {
        SimulationSession session = SessionOf();
        Assert.True(session.Calculate().Succeeded);

        OperationResult<PropagationModel> result = session.SetModel(PropagationModel.DiffractionAndReflection);
        OperationResult<RssiGrid> grid = session.Calculate();

        Assert.True(result.Succeeded);
        Assert.Equal(PropagationModel.DiffractionAndReflection, session.Model);
        Assert.True(grid.Succeeded);
        Assert.Empty(grid.Warnings);
        Assert.Equal([PropagationModel.StraightLine, PropagationModel.Diffraction, PropagationModel.DiffractionAndReflection], PropagationModelInfo.Available);
    }

    [Fact]
    public void An_unknown_model_is_rejected()
    {
        Assert.False(SessionOf().SetModel((PropagationModel)42).Succeeded);
    }

    [Fact]
    public void A_straight_line_query_has_no_paths()
    {
        PointCalculation c = SessionOf().Query(new Vec2(3.3, 0.15)).Value!;

        Assert.Equal(PropagationModel.StraightLine, c.Model);
        Assert.Null(c.Paths);
        Assert.Equal(-92.523, c.ReceivedPowerDbm, 0.02);
        Assert.DoesNotContain("Modelo:", Explanation.Format(c));
        Assert.DoesNotContain("Trayectos", Explanation.Format(c));
    }

    [Fact]
    public void A_diffraction_query_keeps_the_straight_line_data_and_adds_the_paths()
    {
        SimulationSession session = SessionOf();
        session.SetModel(PropagationModel.Diffraction);

        PointCalculation c = session.Query(new Vec2(3.3, 0.15)).Value!;

        Assert.Equal(PropagationModel.Diffraction, c.Model);
        Assert.Equal(-54.249, c.ReceivedPowerDbm, 0.02);
        Assert.Equal(6.3, c.DistanceM, 9);
        Assert.Equal(49.8, c.WallLossDb, 9);
        Assert.Equal("C", Assert.Single(c.Walls).WallId);
        Assert.Equal(2, c.Paths!.Count);
        Assert.Equal(1, c.Paths.Sum(p => p.PowerShare), 9);
    }

    [Fact]
    public void The_explanation_names_the_model_and_lists_the_paths()
    {
        SimulationSession session = SessionOf();
        session.SetModel(PropagationModel.Diffraction);

        string text = Explanation.Format(session.Query(new Vec2(3.3, 0.15)).Value!);

        Assert.Contains("Modelo: Con difracción", text);
        Assert.Contains("Recta AP–punto:", text);
        Assert.Contains("Columna C", text);
        Assert.Contains("Trayectos sumados: 2 (suma de potencias, sin fases)", text);
        Assert.Contains("1. Difractado ×2 en (0; 0) y (0,3; 0) · 6,31 m · pérdida 77,3 dB · 50,0 % de la potencia", text);
        Assert.Contains("2. Difractado ×2 en (0; 0,3) y (0,3; 0,3)", text);
        Assert.Contains("RSSI estimado = -54,25 dBm", text);
        Assert.EndsWith("Estimación del modelo; no es una medición.", text);
    }

    [Fact]
    public void The_explanation_lists_five_paths_and_sums_up_the_rest()
    {
        ReferenceScene reference = ReferenceData.Scene("mixta");
        var engine = new PathEngine(reference.Scene(), reference.AccessPoint, reference.Settings, new PropagationOptions(PruneDb: 500));

        PointCalculation c = PointCalculator.Calculate(engine, new Vec2(6, -0.9));
        string text = Explanation.Format(c);

        Assert.True(c.Paths!.Count > Explanation.MaxListedPaths);
        Assert.Contains($"Trayectos sumados: {c.Paths.Count}", text);
        Assert.Contains("  5. ", text);
        Assert.DoesNotContain("  6. ", text);
        Assert.Contains($"Los otros {c.Paths.Count - 5} aportan el ", text);
    }

    [Fact]
    public void A_direct_path_is_described_as_such()
    {
        var direct = new PathContribution(PathKind.Direct, [], 5, 70, 1);
        var once = new PathContribution(PathKind.Diffracted, [new Vec2(7, 0.5)], 9, 80, 0.1);

        Assert.Equal("Directo", Explanation.DescribePath(direct));
        Assert.Equal("Difractado ×1 en (7; 0,5)", Explanation.DescribePath(once));
    }

    [Fact]
    public void The_grid_with_diffraction_has_signal_in_the_shadow_of_the_column()
    {
        SimulationSession session = SessionOf();
        RssiGrid straight = session.Calculate().Value!;
        session.SetModel(PropagationModel.Diffraction);
        RssiGrid diffraction = session.Calculate().Value!;

        Assert.Equal(straight.Spec, diffraction.Spec);
        Assert.Equal(new Vec2(3.25, 0.25), straight.Spec.CellCenter(14, 4));
        Assert.True(straight[14, 4] < -90);
        Assert.True(diffraction[14, 4] > -60);
        Assert.Equal(straight[2, 7], diffraction[2, 7], 0.5);
    }

    [Fact]
    public void The_query_and_the_grid_agree_at_a_cell_centre()
    {
        SimulationSession session = SessionOf("mixta");
        session.SetCoverageArea(Polygon2.Rectangle(new BoundingBox(new Vec2(-1, -4), new Vec2(10, 7))));
        session.SetModel(PropagationModel.Diffraction);
        RssiGrid grid = session.Calculate().Value!;

        for (int column = 0; column < grid.Spec.Columns; column += 3)
        {
            Vec2 centre = grid.Spec.CellCenter(column, 5);
            if (session.Query(centre) is { Succeeded: true } query)
            {
                Assert.Equal(grid[column, 5], query.Value!.ReceivedPowerDbm, 9);
            }
        }
    }

    [Fact]
    public void Moving_the_access_point_or_changing_the_band_is_seen_by_the_next_query()
    {
        SimulationSession session = SessionOf();
        session.SetModel(PropagationModel.Diffraction);
        var point = new Vec2(3.3, 0.15);
        double first = session.Query(point).Value!.ReceivedPowerDbm;

        session.PlaceAccessPoint(new Vec2(-3, 3));
        double moved = session.Query(point).Value!.ReceivedPowerDbm;
        session.Configure(session.Settings with { Band = Band.Ghz2_4 }, 0.5);
        double otherBand = session.Query(point).Value!.ReceivedPowerDbm;

        Assert.True(moved > first + 5);
        Assert.True(otherBand > moved + 3);
        Assert.Equal(PropagationModel.Diffraction, session.Model);
    }

    [Fact]
    public void Grid_calculation_in_parallel_equals_the_sequential_one()
    {
        ReferenceScene reference = ReferenceData.Scene("mixta");
        var area = Polygon2.Rectangle(new BoundingBox(new Vec2(-1, -4), new Vec2(10, 7)));
        GridSpec spec = GridSpec.Cover(area.Bounds, 0.25);

        RssiGrid grid = GridCalculator.Calculate(reference.Scene(), reference.AccessPoint, reference.Settings, spec, area, PropagationModel.Diffraction);

        var engine = new PathEngine(reference.Scene(), reference.AccessPoint, reference.Settings);
        for (int column = 0; column < spec.Columns; column++)
        {
            for (int row = 0; row < spec.Rows; row++)
            {
                Assert.Equal(engine.ReceivedPowerDbm(spec.CellCenter(column, row)), grid[column, row]);
            }
        }
    }

    [Fact]
    public void With_the_straight_line_model_the_grid_is_the_one_of_always()
    {
        ReferenceScene reference = ReferenceData.Scene("mixta");
        var area = Polygon2.Rectangle(new BoundingBox(new Vec2(-1, -4), new Vec2(10, 7)));
        GridSpec spec = GridSpec.Cover(area.Bounds, 0.5);

        RssiGrid before = GridCalculator.Calculate(reference.Scene(), reference.AccessPoint, reference.Settings, spec, area);
        RssiGrid now = GridCalculator.Calculate(reference.Scene(), reference.AccessPoint, reference.Settings, spec, area, PropagationModel.StraightLine);

        for (int column = 0; column < spec.Columns; column++)
        {
            for (int row = 0; row < spec.Rows; row++) Assert.Equal(before[column, row], now[column, row]);
        }
    }

    [Fact]
    public void The_grid_with_reflections_is_never_below_the_one_with_diffraction()
    {
        ReferenceScene reference = ReferenceData.Scene("mixta");
        GridSpec spec = GridSpec.Cover(Area.Bounds, 0.5);

        RssiGrid diffraction = GridCalculator.Calculate(reference.Scene(), reference.AccessPoint, reference.Settings, spec, Area, PropagationModel.Diffraction);
        RssiGrid reflection = GridCalculator.Calculate(reference.Scene(), reference.AccessPoint, reference.Settings, spec, Area, PropagationModel.DiffractionAndReflection);

        bool anyHigher = false;
        for (int column = 0; column < spec.Columns; column++)
        {
            for (int row = 0; row < spec.Rows; row++)
            {
                double withDiffraction = diffraction[column, row];
                if (double.IsNaN(withDiffraction)) continue;

                Assert.True(reflection[column, row] >= withDiffraction - 0.01);
                anyHigher |= reflection[column, row] > withDiffraction + 0.5;
            }
        }

        Assert.True(anyHigher);
    }

    [Fact]
    public void The_legend_names_the_model()
    {
        var settings = new RadioSettings { Band = Band.Ghz5 };

        Assert.Contains("Modelo: Línea recta", Legend.For(settings).Details);
        Assert.Contains("Modelo: Con difracción", Legend.For(settings, PropagationModel.Diffraction).Details);
        Assert.Contains("Modelo: Con difracción", Legend.ForReinforcementEffect(settings, new EffectScale(5), PropagationModel.Diffraction).Details);
    }

    [Fact]
    public void With_diffraction_the_reinforcement_changes_the_result_much_less()
    {
        ReferenceScene reference = ReferenceData.Scene("columna-sola-2g4");
        SimulationScene scene = reference.Scene();
        GridSpec spec = GridSpec.Cover(Area.Bounds, 0.2);

        ReinforcementEffectMap straight = ReinforcementEffect.Calculate(scene, reference.AccessPoint, reference.Settings, spec, Area)!;
        ReinforcementEffectMap diffraction = ReinforcementEffect.Calculate(scene, reference.AccessPoint, reference.Settings, spec, Area, PropagationModel.Diffraction)!;

        Assert.True(diffraction.AffectedCells < straight.AffectedCells);
        Assert.True(diffraction.AffectedCells > 0);

        var normal = new PathEngine(scene, reference.AccessPoint, reference.Settings);
        var plain = new PathEngine(scene.WithoutReinforcement(), reference.AccessPoint, reference.Settings);
        Vec2 behind = spec.CellCenter(spec.Columns - 3, spec.Rows / 2);
        double expected = plain.ReceivedPowerDbm(behind) - normal.ReceivedPowerDbm(behind);
        double value = diffraction.Grid[spec.Columns - 3, spec.Rows / 2];

        Assert.True(expected < 1);
        if (expected >= EffectScale.VisibleThresholdDb) Assert.Equal(expected, value, 9);
        else Assert.True(double.IsNaN(value));
    }

    [Fact]
    public void The_session_effect_map_follows_the_model()
    {
        SimulationSession session = SessionOf("columna-sola-2g4");
        ReinforcementEffectMap straight = session.CalculateReinforcementEffect(Area).Value!.Map!;
        session.SetModel(PropagationModel.Diffraction);
        ReinforcementEffectMap diffraction = session.CalculateReinforcementEffect(Area).Value!.Map!;

        Assert.True(diffraction.AffectedCells < straight.AffectedCells);
    }
}
