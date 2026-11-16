using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Service;

public class ReinforcementEffectServiceTests
{
    private static FakeDrawing Room(Reinforcement? level = Reinforcement.High)
    {
        var d = new FakeDrawing(FakeDrawing.CatalogWithConcrete);
        d.Add("W1", 0, 0, 10, 0, "brick", 0.15);
        d.Add("W2", 10, 0, 10, 8, "brick", 0.15);
        d.Add("W3", 10, 8, 0, 8, "brick", 0.15);
        d.Add("W4", 0, 8, 0, 0, "brick", 0.15);
        if (level is { } l) d.AddColumn("C1", 5, 3.9, 0.3, l);
        d.SetAccessPoint(new Vec2(1, 4));
        return d;
    }

    [Fact]
    public void It_draws_the_effect_with_its_own_legend_and_file_kind()
    {
        FakeDrawing d = Room();

        OperationResult<ReinforcementEffectSummary> result = d.ShowReinforcementEffect();

        Assert.True(result.Succeeded);
        ReinforcementEffectSummary s = result.Value!;
        Assert.False(s.NothingToShow);
        Assert.Equal(7.5, s.MaxDb, 9);
        Assert.Equal(1, s.ReinforcedColumns);
        Assert.True(s.AffectedCells > 0);
        Assert.Equal(OverlayImageKind.ReinforcementEffect, Assert.Single(d.DrawnKinds));
        Assert.Equal(Legend.ReinforcementEffectTitle, d.LastDrawnLegend!.Title);
        Assert.NotNull(s.ImagePath);
        Assert.Contains("7,5", s.Headline);
    }

    [Fact]
    public void The_resolution_is_lowered_for_this_map_and_the_session_keeps_its_own()
    {
        FakeDrawing d = Room();

        ReinforcementEffectSummary s = d.ShowReinforcementEffect().Value!;

        Assert.True(s.ResolutionLowered);
        Assert.Equal(0.2, s.CellSizeM, 9);
        Assert.Equal(0.5, s.SessionCellSizeM, 9);
        Assert.Equal(0.5, d.Settings.CellSizeM);
        Assert.Contains(s.Warnings, w => w.Contains("0,5") && w.Contains("0,2") && w.Contains("configuración de la sesión no cambió"));
    }

    [Fact]
    public void A_fine_resolution_is_not_reported_as_lowered()
    {
        FakeDrawing d = Room();
        d.Configure(d.Settings with { CellSizeM = 0.1 });

        ReinforcementEffectSummary s = d.ShowReinforcementEffect().Value!;

        Assert.False(s.ResolutionLowered);
        Assert.Equal(0.1, s.CellSizeM, 9);
        Assert.DoesNotContain(s.Warnings, w => w.Contains("se bajó"));
    }

    [Fact]
    public void The_coverage_result_stays_valid_and_unchanged()
    {
        FakeDrawing d = Room();
        CalculationSummary coverage = d.Calculate().Value!;
        RssiGrid grid = d.Session.LastGrid!;

        d.ShowReinforcementEffect();

        Assert.True(d.HasResult);
        Assert.Same(grid, d.Session.LastGrid);
        Assert.Same(coverage, d.LastCalculation);
        Assert.Equal(0.5, d.Session.CellSizeM);
    }

    [Fact]
    public void Without_reinforced_columns_it_says_so_and_draws_nothing()
    {
        FakeDrawing d = Room(level: null);

        OperationResult<ReinforcementEffectSummary> result = d.ShowReinforcementEffect();

        Assert.True(result.Succeeded);
        Assert.True(result.Value!.NothingToShow);
        Assert.Equal(0, result.Value.ReinforcedColumns);
        Assert.Contains("no hay nada que mostrar", result.Value.Headline);
        Assert.DoesNotContain(result.Value.Warnings, w => w.Contains("se bajó"));
        Assert.Equal(0, d.Draws);
    }

    [Fact]
    public void Calculating_again_goes_back_to_the_coverage_map()
    {
        FakeDrawing d = Room();
        d.ShowReinforcementEffect();

        d.Calculate();

        Assert.Equal([OverlayImageKind.ReinforcementEffect, OverlayImageKind.Coverage], d.DrawnKinds);
        Assert.NotEqual(Legend.ReinforcementEffectTitle, d.LastDrawnLegend!.Title);
    }

    [Fact]
    public void Changing_the_fade_keeps_the_legend_of_the_map_that_is_shown()
    {
        FakeDrawing d = Room();
        d.Calculate();
        d.ShowReinforcementEffect();

        d.SetOverlayFade(60);
        Assert.Equal(Legend.ReinforcementEffectTitle, d.LastFadeLegend!.Title);

        d.Calculate();
        d.SetOverlayFade(30);
        Assert.NotEqual(Legend.ReinforcementEffectTitle, d.LastFadeLegend!.Title);
    }

    [Fact]
    public void It_needs_an_access_point()
    {
        var d = new FakeDrawing(FakeDrawing.CatalogWithConcrete);
        d.Add("W1", 0, 0, 10, 0, "brick", 0.15);
        d.Add("W2", 10, 0, 10, 8, "brick", 0.15);
        d.AddColumn("C1", 5, 3.9, 0.3, Reinforcement.Low);

        OperationResult<ReinforcementEffectSummary> result = d.ShowReinforcementEffect();

        Assert.False(result.Succeeded);
        Assert.Contains("AP", result.Errors[0]);
        Assert.Equal(0, d.Draws);
    }

    [Fact]
    public void It_refuses_a_plan_with_validation_errors()
    {
        FakeDrawing d = Room();
        d.Add("W5", 0, 0, 5, 5);

        OperationResult<ReinforcementEffectSummary> result = d.ShowReinforcementEffect();

        Assert.False(result.Succeeded);
        Assert.Equal(0, d.Draws);
    }
}
