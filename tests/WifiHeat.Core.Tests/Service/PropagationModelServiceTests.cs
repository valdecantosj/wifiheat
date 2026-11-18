using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Service;

public class PropagationModelServiceTests
{
    private static FakeDrawing Room(Reinforcement? column = Reinforcement.High)
    {
        var d = new FakeDrawing(FakeDrawing.CatalogWithConcrete);
        d.Add("W1", 0, 0, 10, 0, "brick", 0.15);
        d.Add("W2", 10, 0, 10, 8, "brick", 0.15);
        d.Add("W3", 10, 8, 0, 8, "brick", 0.15);
        d.Add("W4", 0, 8, 0, 0, "brick", 0.15);
        if (column is { } level) d.AddColumn("C1", 5, 3.85, 0.3, level);
        d.SetAccessPoint(new Vec2(1, 4));
        return d;
    }

    [Fact]
    public void Straight_line_is_the_default_and_three_models_are_offered()
    {
        FakeDrawing d = Room();

        Assert.Equal(PropagationModel.StraightLine, d.Settings.Model);
        Assert.Equal(3, d.Settings.MaxDiffractions);
        Assert.Equal([PropagationModel.StraightLine, PropagationModel.Diffraction, PropagationModel.DiffractionAndReflection], d.PropagationModels);
    }

    [Fact]
    public void Settings_written_without_a_model_keep_the_straight_line()
    {
        FakeDrawing d = Room();

        OperationResult<SimulationSettings> result = d.Configure(new SimulationSettings(Band.Ghz5, 17, 2, 1, 0.5, 0.3, 0.25, 20));

        Assert.True(result.Succeeded);
        Assert.Equal(PropagationModel.StraightLine, result.Value!.Model);
        Assert.Equal(PropagationModel.StraightLine, d.Session.Model);
    }

    [Fact]
    public void Configure_changes_the_model_and_invalidates_the_result()
    {
        FakeDrawing d = Room();
        Assert.True(d.Calculate().Succeeded);
        Assert.True(d.HasResult);

        OperationResult<SimulationSettings> result = d.Configure(d.Settings with { Model = PropagationModel.Diffraction, MaxDiffractions = 2 });

        Assert.True(result.Succeeded);
        Assert.Equal(PropagationModel.Diffraction, d.Settings.Model);
        Assert.Equal(2, d.Settings.MaxDiffractions);
        Assert.False(d.HasResult);
    }

    [Fact]
    public void The_model_with_reflections_calculates_without_warnings_when_every_material_reflects()
    {
        FakeDrawing d = Room();

        OperationResult<SimulationSettings> configured = d.Configure(d.Settings with { Model = PropagationModel.DiffractionAndReflection });
        var calculated = d.Calculate();

        Assert.True(configured.Succeeded);
        Assert.Equal(PropagationModel.DiffractionAndReflection, d.Session.Model);
        Assert.True(calculated.Succeeded);
        Assert.DoesNotContain(calculated.Warnings, w => w.Contains("Sin rebote"));
    }

    [Fact]
    public void The_model_with_reflections_warns_about_materials_without_electrical_properties()
    {
        var d = new FakeDrawing();
        d.Add("W1", 0, 0, 10, 0, "brick", 0.15);
        d.Add("W2", 10, 0, 10, 8, "gypsum", 0.15);
        d.Add("W3", 10, 8, 0, 8, "brick", 0.15);
        d.Add("W4", 0, 8, 0, 0, "brick", 0.15);
        d.SetAccessPoint(new Vec2(1, 4));

        Assert.True(d.Configure(d.Settings with { Model = PropagationModel.DiffractionAndReflection }).Succeeded);
        var calculated = d.Calculate();

        Assert.True(calculated.Succeeded);
        Assert.Contains(calculated.Warnings, w => w.Contains("Sin rebote en Ladrillo, Yeso"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void The_limit_of_diffractions_is_validated(int value)
    {
        FakeDrawing d = Room();
        SimulationSettings before = d.Settings;

        OperationResult<SimulationSettings> result = d.Configure(before with { Model = PropagationModel.Diffraction, MaxDiffractions = value });

        Assert.False(result.Succeeded);
        Assert.Contains(result.Errors, e => e.Contains("1 a 4"));
        Assert.Equal(before, d.Settings);
    }

    [Fact]
    public void The_summary_and_the_legend_name_the_model()
    {
        FakeDrawing d = Room();

        CalculationSummary straight = d.Calculate().Value!;
        Assert.Equal(PropagationModel.StraightLine, straight.Model);
        Assert.Contains("Modelo: Línea recta", d.LastDrawnLegend!.Details);

        d.Configure(d.Settings with { Model = PropagationModel.Diffraction });
        CalculationSummary diffraction = d.Calculate().Value!;

        Assert.Equal(PropagationModel.Diffraction, diffraction.Model);
        Assert.Equal(PropagationModel.Diffraction, d.LastCalculation!.Model);
        Assert.Contains("Modelo: Con difracción", d.LastDrawnLegend!.Details);
        Assert.Equal(straight.Cells, diffraction.Cells);
        Assert.True(diffraction.MinDbm > straight.MinDbm + 5);
    }

    [Fact]
    public void A_query_behind_the_column_gets_the_signal_that_goes_around_it()
    {
        FakeDrawing d = Room();
        var behind = new Vec2(9, 4);
        QueryResult straight = d.Query(behind).Value!;

        d.Configure(d.Settings with { Model = PropagationModel.Diffraction });
        QueryResult diffraction = d.Query(behind).Value!;

        Assert.True(diffraction.ReceivedPowerDbm > straight.ReceivedPowerDbm + 10);
        Assert.Equal(diffraction.ReceivedPowerDbm, diffraction.Calculation.ReceivedPowerDbm);
        Assert.Equal(SignalScale.Classify(diffraction.ReceivedPowerDbm), diffraction.Level);
        Assert.Equal(straight.Calculation.WallLossDb, diffraction.Calculation.WallLossDb, 9);
        Assert.NotEmpty(diffraction.Calculation.Paths!);
        Assert.Contains("Modelo: Con difracción", diffraction.Explanation);
        Assert.Contains("Trayectos sumados:", diffraction.Explanation);
        Assert.DoesNotContain("Modelo:", straight.Explanation);
    }

    [Fact]
    public void Without_obstacles_in_the_way_both_models_agree()
    {
        FakeDrawing d = Room(column: null);
        var point = new Vec2(6, 5);
        double straight = d.Query(point).Value!.ReceivedPowerDbm;

        d.Configure(d.Settings with { Model = PropagationModel.Diffraction });

        Assert.Equal(straight, d.Query(point).Value!.ReceivedPowerDbm, 0.05);
    }

    [Fact]
    public void Going_back_to_the_straight_line_gives_the_values_of_before()
    {
        FakeDrawing d = Room();
        CalculationSummary first = d.Calculate().Value!;

        d.Configure(d.Settings with { Model = PropagationModel.Diffraction });
        d.Calculate();
        d.Configure(d.Settings with { Model = PropagationModel.StraightLine });
        CalculationSummary again = d.Calculate().Value!;

        Assert.Equal(first.MinDbm, again.MinDbm);
        Assert.Equal(first.MaxDbm, again.MaxDbm);
        Assert.Equal(PropagationModel.StraightLine, again.Model);
    }

    [Fact]
    public void The_fade_keeps_the_legend_of_the_model()
    {
        FakeDrawing d = Room();
        d.Configure(d.Settings with { Model = PropagationModel.Diffraction });
        d.Calculate();

        d.SetOverlayFade(60);

        Assert.Contains("Modelo: Con difracción", d.LastFadeLegend!.Details);
    }

    [Fact]
    public void The_reinforcement_effect_names_the_model_when_it_is_not_the_straight_line()
    {
        FakeDrawing d = Room();
        d.Configure(d.Settings with { Model = PropagationModel.Diffraction });

        ReinforcementEffectSummary s = d.ShowReinforcementEffect().Value!;

        Assert.Equal(PropagationModel.Diffraction, s.Model);
        Assert.True(s.MaxDb <= 7.5 + 1e-9);
        if (s.NothingToShow)
        {
            Assert.Contains(s.Details, line => line.Contains("con difracción"));
        }
        else
        {
            Assert.Contains("(modelo con difracción)", s.Headline);
            Assert.Contains("Modelo: Con difracción", d.LastDrawnLegend!.Details);
        }
    }

    [Fact]
    public void The_reinforcement_effect_with_the_straight_line_reads_as_before()
    {
        FakeDrawing d = Room();

        ReinforcementEffectSummary s = d.ShowReinforcementEffect().Value!;

        Assert.Equal(PropagationModel.StraightLine, s.Model);
        Assert.Equal("Efecto máximo de la armadura: 7,5 dB", s.Headline);
    }
}
