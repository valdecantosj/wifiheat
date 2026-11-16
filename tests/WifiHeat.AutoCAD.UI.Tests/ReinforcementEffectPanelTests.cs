using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using Xunit;

namespace WifiHeat.AutoCAD.UI.Tests;

public class ReinforcementEffectPanelTests
{
    private static Sample WithColumn(Reinforcement? level = Reinforcement.High) => Sample.WithConcrete(d =>
    {
        d.Add("A1", 0, 0, 10, 0, "brick", 0.2);
        d.Add("A2", 10, 0, 10, 8, "brick", 0.2);
        d.Add("A3", 10, 8, 0, 8, "brick", 0.2);
        d.Add("A4", 0, 8, 0, 0, "brick", 0.2);
        if (level is { } l) d.AddColumn("C1", 5, 3.9, 0.3, l);
        d.SetAccessPoint(new Vec2(1, 4));
    });

    [Fact]
    public async Task The_button_shows_the_maximum_cells_and_resolution_in_a_message()
    {
        Sample s = WithColumn();

        await s.Panel.Calculation.ShowEffectAsync();

        PanelMessage message = s.Panel.Messages.Current!;
        Assert.Equal(StatusKind.Warning, message.Kind);
        Assert.Equal("Efecto máximo de la armadura: 7,5 dB", message.Title);
        Assert.Contains("celda(s) afectada(s)", message.Detail);
        Assert.Contains("resolución 0,2 m", message.Detail);
        Assert.Contains("se bajó a 0,2 m solo para este mapa", message.Detail);
        Assert.Equal(OverlayImageKind.ReinforcementEffect, Assert.Single(s.Drawing.DrawnKinds));
    }

    [Fact]
    public async Task With_a_fine_session_resolution_the_message_is_a_success_without_the_resolution_warning()
    {
        Sample s = WithColumn();
        s.Drawing.Configure(s.Drawing.Settings with { CellSizeM = 0.1 });

        await s.Panel.Calculation.ShowEffectAsync();

        PanelMessage message = s.Panel.Messages.Current!;
        Assert.Equal(StatusKind.Success, message.Kind);
        Assert.Contains("resolución 0,1 m", message.Detail);
        Assert.DoesNotContain("se bajó", message.Detail);
    }

    [Fact]
    public async Task Without_reinforced_columns_the_message_is_a_clear_warning_and_nothing_is_drawn()
    {
        Sample s = WithColumn(level: null);

        await s.Panel.Calculation.ShowEffectAsync();

        PanelMessage message = s.Panel.Messages.Current!;
        Assert.Equal(StatusKind.Warning, message.Kind);
        Assert.Equal("No hay columnas con armadura: no hay nada que mostrar", message.Title);
        Assert.Contains("Marcar las columnas", message.Detail);
        Assert.Empty(s.Drawing.DrawnKinds);
        Assert.False(s.Panel.Calculation.ShowingEffect);
    }

    [Fact]
    public async Task Without_an_access_point_it_is_an_error_and_the_button_is_not_ready()
    {
        Sample s = Sample.WithConcrete(d =>
        {
            d.Add("A1", 0, 0, 10, 0, "brick", 0.2);
            d.Add("A2", 10, 0, 10, 8, "brick", 0.2);
            d.AddColumn("C1", 5, 3.9, 0.3, Reinforcement.Low);
        });

        Assert.False(s.Panel.Calculation.IsReady);
        await s.Panel.Calculation.ShowEffectAsync();

        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
        Assert.Contains("AP", s.Panel.Messages.Text);
    }

    [Fact]
    public async Task While_the_effect_is_shown_the_legend_is_its_own()
    {
        Sample s = WithColumn();
        CalculationViewModel calc = s.Panel.Calculation;
        Assert.Equal("Leyenda: RSSI estimado", calc.LegendTitle);
        Assert.Equal(6, calc.Legend.Count);

        await calc.ShowEffectAsync();

        Assert.True(calc.ShowingEffect);
        Assert.Equal("Leyenda: Pérdida que agrega la armadura (dB)", calc.LegendTitle);
        Assert.Equal(EffectScale.Steps, calc.Legend.Count);
        Assert.Equal("6 a 7,5 dB", calc.Legend[0].Label);
    }

    [Fact]
    public async Task Calculating_goes_back_to_the_coverage_legend()
    {
        Sample s = WithColumn();
        CalculationViewModel calc = s.Panel.Calculation;
        await calc.ShowEffectAsync();

        await calc.CalculateAsync();

        Assert.False(calc.ShowingEffect);
        Assert.Equal("Leyenda: RSSI estimado", calc.LegendTitle);
        Assert.Equal(6, calc.Legend.Count);
    }

    [Fact]
    public async Task Showing_the_effect_keeps_the_coverage_result_and_the_session_resolution()
    {
        Sample s = WithColumn();
        await s.Panel.Calculation.CalculateAsync();
        CalculationSummary summary = s.Drawing.LastCalculation!;

        await s.Panel.Calculation.ShowEffectAsync();

        Assert.Equal(MapState.Current, s.Panel.Calculation.State);
        Assert.Same(summary, s.Drawing.LastCalculation);
        Assert.Equal("0,50", s.Panel.AccessPoint.ResolutionText);
        Assert.True(s.Panel.Calculation.HasSummary);
    }

    [Fact]
    public async Task The_fade_slider_keeps_working_while_the_effect_is_shown()
    {
        Sample s = WithColumn();
        await s.Panel.Calculation.ShowEffectAsync();

        s.Panel.Calculation.FadePercent = 70;

        Assert.Equal(70, s.Drawing.Settings.OverlayFadePercent);
        Assert.Equal("Pérdida que agrega la armadura (dB)", s.Drawing.LastFadeLegend!.Title);
        Assert.True(s.Panel.Calculation.ShowingEffect);
    }

    [Fact]
    public async Task The_panel_is_not_left_busy()
    {
        Sample s = WithColumn();

        await s.Panel.Calculation.ShowEffectAsync();

        Assert.False(s.Panel.IsBusy);
        Assert.Null(s.Panel.ActiveMode);
    }
}
