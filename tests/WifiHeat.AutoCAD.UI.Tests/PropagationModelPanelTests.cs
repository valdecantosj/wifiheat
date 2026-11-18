using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;
using Xunit;

namespace WifiHeat.AutoCAD.UI.Tests;

public class PropagationModelPanelTests
{
    private static Sample WithColumn() => Sample.WithConcrete(d =>
    {
        d.Add("A1", 0, 0, 10, 0, "brick", 0.2);
        d.Add("A2", 10, 0, 10, 8, "brick", 0.2);
        d.Add("A3", 10, 8, 0, 8, "brick", 0.2);
        d.Add("A4", 0, 8, 0, 0, "brick", 0.2);
        d.AddColumn("C1", 5, 3.85, 0.3, Reinforcement.High);
        d.SetAccessPoint(new Vec2(1, 4));
    });

    private static ModelOption Option(Sample s, PropagationModel model) => s.Panel.Calculation.Models.Single(m => m.Model == model);

    [Fact]
    public void The_list_offers_straight_line_diffraction_and_reflections()
    {
        CalculationViewModel calc = Sample.Empty().Panel.Calculation;

        Assert.Equal(["Línea recta", "Con difracción", "Con difracción y reflexiones"], calc.Models.Select(m => m.Label));
        Assert.Equal(PropagationModel.StraightLine, calc.SelectedModel.Model);
        Assert.Contains("Recta del AP", calc.ModelHelp);
    }

    [Fact]
    public void The_help_line_follows_the_choice()
    {
        Sample s = WithColumn();
        var changed = new List<string?>();
        s.Panel.Calculation.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.Diffraction);

        Assert.Contains("rodea", s.Panel.Calculation.ModelHelp);
        Assert.Contains(nameof(CalculationViewModel.ModelHelp), changed);
        Assert.Contains(nameof(CalculationViewModel.SelectedModel), changed);
    }

    [Fact]
    public void Choosing_a_model_writes_it_to_the_service()
    {
        Sample s = WithColumn();

        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.Diffraction);

        Assert.Equal(PropagationModel.Diffraction, s.Drawing.Settings.Model);
        Assert.Equal(StatusKind.Success, s.Panel.Messages.Kind);
        Assert.Equal("Modelo de propagación: Con difracción", s.Panel.Messages.Current!.Title);
        Assert.Equal(MapState.None, s.Panel.Calculation.State);
    }

    [Fact]
    public async Task Changing_the_model_leaves_the_map_outdated_with_a_warning()
    {
        Sample s = WithColumn();
        await s.Panel.Calculation.CalculateAsync();
        Assert.Equal(MapState.Current, s.Panel.Calculation.State);

        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.Diffraction);

        Assert.Equal(MapState.Outdated, s.Panel.Calculation.State);
        PanelMessage message = s.Panel.Messages.Current!;
        Assert.Equal(StatusKind.Warning, message.Kind);
        Assert.Equal("Mapa desactualizado", message.Title);
        Assert.Contains("«Con difracción»", message.Detail);
        Assert.Equal("Línea recta", s.Panel.Calculation.ModelText);
    }

    [Fact]
    public async Task The_summary_names_the_model_of_the_calculation()
    {
        Sample s = WithColumn();
        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.Diffraction);

        await s.Panel.Calculation.CalculateAsync();

        Assert.Equal(MapState.Current, s.Panel.Calculation.State);
        Assert.Equal("Con difracción", s.Panel.Calculation.ModelText);
        Assert.Equal(StatusKind.Success, s.Panel.Messages.Kind);
        Assert.Contains("Modelo: Con difracción.", s.Panel.Messages.Current!.Detail);
        Assert.Contains("Modelo: Con difracción", s.Drawing.LastDrawnLegend!.Details);
    }

    [Fact]
    public async Task The_busy_text_names_the_model_when_it_is_not_the_straight_line()
    {
        Sample s = WithColumn();
        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.Diffraction);
        string text = "";
        s.Interaction.OnYield = () => text = s.Panel.BusyText;

        await s.Panel.Calculation.CalculateAsync();

        Assert.Equal("Calculando el mapa (con difracción)…", text);
    }

    [Fact]
    public void The_list_shows_a_model_chosen_by_command()
    {
        Sample s = WithColumn();

        s.Drawing.Configure(s.Drawing.Settings with { Model = PropagationModel.Diffraction });
        s.Panel.Refresh();

        Assert.Equal(PropagationModel.Diffraction, s.Panel.Calculation.SelectedModel.Model);
        Assert.False(s.Panel.Messages.HasCurrent);
    }

    [Fact]
    public void With_a_command_in_progress_the_choice_goes_back()
    {
        Sample s = WithColumn();
        s.Interaction.BusyReason = "AutoCAD está ejecutando LINE.";

        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.Diffraction);

        Assert.Equal(PropagationModel.StraightLine, s.Panel.Calculation.SelectedModel.Model);
        Assert.Equal(PropagationModel.StraightLine, s.Drawing.Settings.Model);
    }

    [Fact]
    public void Other_parameters_keep_the_model()
    {
        Sample s = WithColumn();
        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.Diffraction);

        s.Panel.AccessPoint.TxPowerText = "17";

        Assert.Equal(PropagationModel.Diffraction, s.Drawing.Settings.Model);
        Assert.Equal(17, s.Drawing.Settings.TxPowerDbm);
        Assert.Equal(PropagationModel.Diffraction, s.Panel.Calculation.SelectedModel.Model);
    }

    [Fact]
    public async Task A_straight_line_query_shows_no_paths()
    {
        Sample s = WithColumn();
        s.Interaction.Points.Enqueue(new Vec2(9, 4));

        await s.Panel.Query.QueryAsync();

        Assert.False(s.Panel.Query.HasPaths);
        Assert.Empty(s.Panel.Query.Paths);
        Assert.Equal("", s.Panel.Query.PathsTitle);
        Assert.Equal("", s.Panel.Query.PathsNote);
    }

    [Fact]
    public async Task A_diffraction_query_lists_the_paths_and_explains_them()
    {
        Sample s = WithColumn();
        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.Diffraction);
        s.Interaction.Points.Enqueue(new Vec2(9, 4));

        await s.Panel.Query.QueryAsync();

        QueryViewModel query = s.Panel.Query;
        Assert.True(query.HasPaths);
        Assert.NotEmpty(query.Paths);
        Assert.StartsWith("Trayectos sumados: ", query.PathsTitle);
        Assert.Equal("Con difracción", query.ModelText);
        Assert.Contains("recta AP–punto", query.PathsNote);
        Assert.StartsWith("Difractado ×", query.Paths[0].Description);
        Assert.EndsWith(" %", query.Paths[0].ShareText);
        Assert.Contains(" m · pérdida ", query.Paths[0].DetailText);
        Assert.Contains("Modelo: Con difracción", query.Explanation);
        Assert.Contains("Trayectos sumados:", query.Explanation);
        Assert.Single(query.Crossings);
    }

    [Fact]
    public async Task A_query_with_reflections_lists_the_bounces()
    {
        Sample s = WithColumn();
        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.DiffractionAndReflection);
        s.Interaction.Points.Enqueue(new Vec2(9, 4));

        await s.Panel.Query.QueryAsync();

        QueryViewModel query = s.Panel.Query;
        Assert.True(query.HasPaths);
        Assert.Equal("Con difracción y reflexiones", query.ModelText);
        Assert.Contains("Reflejado en muro ", query.Explanation);
        Assert.Contains(query.Paths, p => p.Description.StartsWith("Reflejado en muro "));
    }

    [Fact]
    public async Task A_query_with_reflections_shows_the_walls_where_the_signal_bounces()
    {
        Sample s = WithColumn();
        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.DiffractionAndReflection);
        s.Interaction.Points.Enqueue(new Vec2(9, 4));

        await s.Panel.Query.QueryAsync();

        QueryViewModel query = s.Panel.Query;
        Assert.True(query.HasReflectionModel);
        Assert.True(query.HasReflections);
        Assert.Equal($"Muros donde rebota: {query.Reflections.Count}", query.ReflectionsTitle);
        Assert.All(query.Reflections, r => Assert.Contains(r.Handle, new[] { "A1", "A2", "A3", "A4", "C1" }));
        Assert.Contains(query.Reflections, r => r.Obstacle == "Muro A1" && r.Material == "Ladrillo");
        Assert.All(query.Reflections, r => Assert.EndsWith(" dB", r.LossText));
        Assert.All(query.Reflections, r => Assert.Contains("° desde la normal · trayecto ", r.DetailText));
        Assert.Contains("Muros donde rebota:", query.Explanation);

        query.ShowReflections();

        Assert.Equal(query.Reflections.Select(r => r.Handle).Distinct(), s.Interaction.Shown[^1].Handles);
        Assert.False(s.Interaction.Shown[^1].Zoom);
    }

    [Fact]
    public async Task Without_reflections_the_bounce_list_is_hidden()
    {
        Sample s = WithColumn();
        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.Diffraction);
        s.Interaction.Points.Enqueue(new Vec2(9, 4));

        await s.Panel.Query.QueryAsync();

        Assert.False(s.Panel.Query.HasReflectionModel);
        Assert.Empty(s.Panel.Query.Reflections);
        Assert.Equal("", s.Panel.Query.ReflectionsTitle);

        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.DiffractionAndReflection);

        Assert.True(s.Panel.Query.HasReflectionModel);
        Assert.NotEmpty(s.Panel.Query.Reflections);

        s.Panel.Query.Clear();

        Assert.False(s.Panel.Query.HasReflectionModel);
        Assert.Empty(s.Panel.Query.Reflections);
    }

    [Fact]
    public async Task The_query_follows_a_change_of_model()
    {
        Sample s = WithColumn();
        s.Interaction.Points.Enqueue(new Vec2(9, 4));
        await s.Panel.Query.QueryAsync();
        string straight = s.Panel.Query.ReceivedText;

        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.Diffraction);

        Assert.True(s.Panel.Query.HasPaths);
        Assert.NotEqual(straight, s.Panel.Query.ReceivedText);

        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.StraightLine);

        Assert.False(s.Panel.Query.HasPaths);
        Assert.Equal(straight, s.Panel.Query.ReceivedText);
    }

    [Fact]
    public async Task Clearing_the_query_clears_the_paths()
    {
        Sample s = WithColumn();
        s.Panel.Calculation.SelectedModel = Option(s, PropagationModel.Diffraction);
        s.Interaction.Points.Enqueue(new Vec2(9, 4));
        await s.Panel.Query.QueryAsync();

        s.Panel.Query.Clear();

        Assert.False(s.Panel.Query.HasPaths);
        Assert.Empty(s.Panel.Query.Paths);
    }
}
