using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Service;
using Xunit;

namespace WifiHeat.AutoCAD.UI.Tests;

public class AccessPointAndCalculationTests
{
    [Fact]
    public void Without_access_point_the_section_says_so()
    {
        Sample s = Sample.Room();

        Assert.False(s.Panel.AccessPoint.HasAccessPoint);
        Assert.Equal("Falta colocar el AP.", s.Panel.AccessPoint.PositionSummary);
        Assert.Equal("", s.Panel.AccessPoint.XText);
        Assert.Equal("Falta colocar el AP.", s.Panel.Calculation.Readiness);
    }

    [Fact]
    public void Defaults_show_band_frequency_power_and_resolution()
    {
        AccessPointViewModel ap = Sample.Room().Panel.AccessPoint;

        Assert.Equal(Band.Ghz2_4, ap.SelectedBand.Band);
        Assert.Equal("2437 MHz", ap.FrequencyText);
        Assert.Equal("20", ap.TxPowerText);
        Assert.Equal("0", ap.TxGainText);
        Assert.Equal("0,50", ap.ResolutionText);
    }

    [Fact]
    public async Task Picking_a_point_places_the_access_point()
    {
        Sample s = Sample.Room();
        s.Interaction.Points.Enqueue(new Vec2(2.5, 4));

        await s.Panel.AccessPoint.PickAsync();

        Assert.Equal(new Vec2(2.5, 4), s.Drawing.AccessPoint!.PositionM);
        Assert.True(s.Panel.AccessPoint.HasAccessPoint);
        Assert.Equal("2,5", s.Panel.AccessPoint.XText);
        Assert.Equal("4", s.Panel.AccessPoint.YText);
        Assert.Single(s.Drawing.SavedAccessPoints);
        Assert.True(s.Panel.Calculation.IsReady);
    }

    [Fact]
    public async Task Cancelling_the_pick_leaves_the_access_point_alone()
    {
        Sample s = Sample.RoomWithAccessPoint();

        await s.Panel.AccessPoint.PickAsync();

        Assert.Equal(new Vec2(2, 4), s.Drawing.AccessPoint!.PositionM);
        Assert.Equal(StatusKind.Info, s.Panel.Messages.Kind);
    }

    [Fact]
    public void Typing_both_coordinates_places_a_new_access_point()
    {
        Sample s = Sample.Room();

        s.Panel.AccessPoint.XText = "3";
        Assert.Null(s.Drawing.AccessPoint);
        s.Panel.AccessPoint.YText = "4,5";

        Assert.Equal(new Vec2(3, 4.5), s.Drawing.AccessPoint!.PositionM);
    }

    [Fact]
    public void Editing_a_coordinate_moves_the_access_point()
    {
        Sample s = Sample.RoomWithAccessPoint();

        s.Panel.AccessPoint.XText = "7.25";

        Assert.Equal(new Vec2(7.25, 4), s.Drawing.AccessPoint!.PositionM);
        Assert.Equal("7,25", s.Panel.AccessPoint.XText);
    }

    [Fact]
    public void A_coordinate_that_is_not_a_number_is_rejected_and_restored()
    {
        Sample s = Sample.RoomWithAccessPoint();

        s.Panel.AccessPoint.YText = "arriba";

        Assert.Equal(new Vec2(2, 4), s.Drawing.AccessPoint!.PositionM);
        Assert.Equal("4", s.Panel.AccessPoint.YText);
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
    }

    [Fact]
    public void Changing_the_band_updates_frequency_and_the_saved_marker()
    {
        Sample s = Sample.RoomWithAccessPoint();

        s.Panel.AccessPoint.SelectedBand = s.Panel.AccessPoint.Bands.Single(b => b.Band == Band.Ghz5);

        Assert.Equal(Band.Ghz5, s.Drawing.Settings.Band);
        Assert.Equal("5180 MHz", s.Panel.AccessPoint.FrequencyText);
        Assert.Equal(Band.Ghz5, s.Drawing.SavedAccessPoints[^1].Settings.Band);
    }

    [Fact]
    public void Every_link_parameter_reaches_the_service()
    {
        Sample s = Sample.RoomWithAccessPoint();
        AccessPointViewModel ap = s.Panel.AccessPoint;

        ap.TxPowerText = "17,5";
        ap.TxGainText = "2";
        ap.RxGainText = "1,5";
        ap.TxCableText = "0,5";
        ap.RxCableText = "0,25";
        ap.ResolutionText = "0,25";

        SimulationSettings settings = s.Drawing.Settings;
        Assert.Equal(17.5, settings.TxPowerDbm);
        Assert.Equal(2, settings.TxAntennaGainDbi);
        Assert.Equal(1.5, settings.RxAntennaGainDbi);
        Assert.Equal(0.5, settings.TxCableLossDb);
        Assert.Equal(0.25, settings.RxCableLossDb);
        Assert.Equal(0.25, settings.CellSizeM);
        Assert.Equal("0,25", ap.ResolutionText);
    }

    [Theory]
    [InlineData(nameof(AccessPointViewModel.TxPowerText), "18")]
    [InlineData(nameof(AccessPointViewModel.TxGainText), "3")]
    [InlineData(nameof(AccessPointViewModel.RxGainText), "3")]
    [InlineData(nameof(AccessPointViewModel.TxCableText), "1")]
    [InlineData(nameof(AccessPointViewModel.RxCableText), "1")]
    [InlineData(nameof(AccessPointViewModel.ResolutionText), "1")]
    public async Task Each_parameter_change_marks_the_map_as_outdated(string property, string value)
    {
        Sample s = await Sample.CalculatedAsync();
        Assert.Equal(MapState.Current, s.Panel.Calculation.State);

        typeof(AccessPointViewModel).GetProperty(property)!.SetValue(s.Panel.AccessPoint, value);

        Assert.Equal(MapState.Outdated, s.Panel.Calculation.State);
        Assert.Contains("desactualizado", s.Panel.Messages.Text);
    }

    [Fact]
    public async Task Band_and_position_changes_mark_the_map_as_outdated()
    {
        Sample s = await Sample.CalculatedAsync();
        s.Panel.AccessPoint.SelectedBand = s.Panel.AccessPoint.Bands[1];
        Assert.True(s.Panel.Calculation.IsOutdated);

        await s.Panel.Calculation.CalculateAsync();
        s.Panel.AccessPoint.XText = "6";

        Assert.True(s.Panel.Calculation.IsOutdated);
    }

    [Fact]
    public async Task Retyping_the_same_value_keeps_the_map_current()
    {
        Sample s = await Sample.CalculatedAsync();

        s.Panel.AccessPoint.TxPowerText = "20,0";

        Assert.Equal(MapState.Current, s.Panel.Calculation.State);
        Assert.Equal("20", s.Panel.AccessPoint.TxPowerText);
    }

    [Theory]
    [InlineData(nameof(AccessPointViewModel.TxPowerText), "99", "20")]
    [InlineData(nameof(AccessPointViewModel.TxPowerText), "mucho", "20")]
    [InlineData(nameof(AccessPointViewModel.TxCableText), "-1", "0")]
    [InlineData(nameof(AccessPointViewModel.ResolutionText), "0,01", "0,50")]
    public void Invalid_parameter_is_rejected_and_the_previous_value_restored(string property, string value, string restored)
    {
        Sample s = Sample.RoomWithAccessPoint();
        SimulationSettings before = s.Drawing.Settings;
        var info = typeof(AccessPointViewModel).GetProperty(property)!;

        info.SetValue(s.Panel.AccessPoint, value);

        Assert.Equal(before, s.Drawing.Settings);
        Assert.Equal(restored, info.GetValue(s.Panel.AccessPoint));
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
    }

    [Fact]
    public void Readiness_follows_the_missing_steps()
    {
        Assert.Equal("No hay muros.", Sample.Empty().Panel.Calculation.Readiness);
        Assert.Equal("Corregir 5 error(es) del plano.", Sample.Room(material: null).Panel.Calculation.Readiness);
        Assert.Equal("", Sample.RoomWithAccessPoint().Panel.Calculation.Readiness);
    }

    [Fact]
    public async Task Calculate_shows_busy_state_then_the_summary()
    {
        Sample s = Sample.RoomWithAccessPoint();
        bool busyDuringYield = false;
        s.Interaction.OnYield = () => busyDuringYield = s.Panel.IsBusy && s.Panel.BusyText.Length > 0;

        await s.Panel.Calculation.CalculateAsync();

        CalculationViewModel c = s.Panel.Calculation;
        Assert.True(busyDuringYield);
        Assert.False(s.Panel.IsBusy);
        Assert.Equal(MapState.Current, c.State);
        Assert.Equal("Mapa al día", c.StateText);
        Assert.True(c.HasSummary);
        Assert.Equal("320 (20 × 16, 0,50 m)", c.CellsText);
        Assert.EndsWith(" dBm", c.MinText);
        Assert.EndsWith(" dBm", c.MaxText);
        Assert.Contains("ms de cálculo", c.TimeText);
        Assert.Equal(1, s.Drawing.Draws);
        Assert.Equal(StatusKind.Success, s.Panel.Messages.Kind);
    }

    [Fact]
    public async Task Calculate_with_errors_lists_them_and_stays_without_map()
    {
        Sample s = Sample.Room(material: null);

        await s.Panel.Calculation.CalculateAsync();

        Assert.Equal(MapState.None, s.Panel.Calculation.State);
        Assert.Equal("Sin calcular", s.Panel.Calculation.StateText);
        Assert.True(s.Panel.Calculation.HasProblems);
        Assert.Contains(s.Panel.Calculation.Problems, p => p.Contains("A1"));
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
        Assert.Equal(0, s.Drawing.Draws);
    }

    [Fact]
    public async Task Problems_are_cleared_by_a_successful_calculation()
    {
        Sample s = Sample.Room();
        await s.Panel.Calculation.CalculateAsync();
        Assert.True(s.Panel.Calculation.HasProblems);

        s.Interaction.Points.Enqueue(new Vec2(2, 4));
        await s.Panel.AccessPoint.PickAsync();
        await s.Panel.Calculation.CalculateAsync();

        Assert.False(s.Panel.Calculation.HasProblems);
    }

    [Fact]
    public async Task A_wall_change_marks_the_map_as_outdated_on_refresh()
    {
        Sample s = await Sample.CalculatedAsync();
        s.Drawing.Add("C9", 2, 2, 3, 2, "gypsum", 0.1);

        s.Panel.Refresh();

        Assert.Equal(MapState.Outdated, s.Panel.Calculation.State);
        Assert.True(s.Panel.Calculation.HasSummary);
    }

    [Fact]
    public void A_map_calculated_by_command_is_shown_after_refresh()
    {
        Sample s = Sample.RoomWithAccessPoint();
        s.Drawing.Calculate();

        s.Panel.Refresh();

        Assert.Equal(MapState.Current, s.Panel.Calculation.State);
        Assert.Equal("320 (20 × 16, 0,50 m)", s.Panel.Calculation.CellsText);
    }

    [Fact]
    public async Task Fade_is_applied_to_the_drawn_map_without_recalculating()
    {
        Sample s = await Sample.CalculatedAsync();
        Assert.Equal(40, s.Panel.Calculation.FadePercent);

        s.Panel.Calculation.FadePercent = 65;

        Assert.Equal([65], s.Drawing.FadesApplied);
        Assert.Equal(1, s.Drawing.Draws);
        Assert.Equal(MapState.Current, s.Panel.Calculation.State);
        Assert.Equal("65 %", s.Panel.Calculation.FadeText);
    }

    [Fact]
    public void Fade_out_of_range_is_restored()
    {
        Sample s = Sample.Room();

        s.Panel.Calculation.FadePercent = 95;

        Assert.Equal(40, s.Panel.Calculation.FadePercent);
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
        Assert.Equal(0, s.Panel.Calculation.MinFade);
        Assert.Equal(90, s.Panel.Calculation.MaxFade);
    }

    [Fact]
    public async Task Coverage_area_can_be_a_polyline_or_the_wall_rectangle()
    {
        Sample s = await Sample.CalculatedAsync();
        Assert.Equal("Rectángulo de los muros", s.Panel.Calculation.AreaText);
        s.Drawing.Polylines["2F"] = new Polygon2([new Vec2(1, 1), new Vec2(4, 1), new Vec2(4, 3), new Vec2(1, 3)]);
        s.Interaction.Polyline = "2F";

        await s.Panel.Calculation.PickAreaAsync();

        Assert.True(s.Panel.Calculation.HasCustomArea);
        Assert.Equal("Polilínea 2F · 4 vértices · 6 m²", s.Panel.Calculation.AreaText);
        Assert.True(s.Panel.Calculation.IsOutdated);

        s.Panel.Calculation.UseRectangle();

        Assert.False(s.Panel.Calculation.HasCustomArea);
        Assert.Equal("Rectángulo de los muros", s.Panel.Calculation.AreaText);
    }

    [Fact]
    public async Task A_rejected_polyline_keeps_the_area_and_reports_why()
    {
        Sample s = Sample.RoomWithAccessPoint();
        s.Interaction.Polyline = "FFFF";

        await s.Panel.Calculation.PickAreaAsync();

        Assert.False(s.Panel.Calculation.HasCustomArea);
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
    }

    [Fact]
    public void Legend_has_the_six_colours_of_the_scale()
    {
        IReadOnlyList<LegendEntryViewModel> legend = Sample.Empty().Panel.Calculation.Legend;

        Assert.Equal(6, legend.Count);
        Assert.Equal("#008000", legend[0].ColorHex);
        Assert.Equal("≥ −50 dBm", legend[0].Label);
        Assert.Equal("Rojo", legend[5].ColorName);
        Assert.Equal("#DC1E1E", legend[5].ColorHex);
    }
}
