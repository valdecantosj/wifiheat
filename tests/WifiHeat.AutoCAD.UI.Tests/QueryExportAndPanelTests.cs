using System.Reflection;
using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.Core.Geometry;
using Xunit;

namespace WifiHeat.AutoCAD.UI.Tests;

public class QueryExportAndPanelTests
{
    [Fact]
    public void Before_any_query_the_card_is_empty()
    {
        QueryViewModel q = Sample.RoomWithAccessPoint().Panel.Query;

        Assert.False(q.HasResult);
        Assert.Equal("Todavía no se consultó ningún punto.", q.EmptyText);
        Assert.Equal("", q.ReceivedText);
    }

    [Fact]
    public async Task Query_shows_distance_walls_losses_power_and_class()
    {
        Sample s = Sample.RoomWithAccessPoint();
        s.Interaction.Points.Enqueue(new Vec2(8, 4));

        await s.Panel.Query.QueryAsync();

        QueryViewModel q = s.Panel.Query;
        Assert.True(q.HasResult);
        Assert.False(q.IsActive);
        Assert.Equal("(8; 4) m", q.PointText);
        Assert.Equal("6,00 m", q.DistanceText);
        Assert.Equal("55,75 dB", q.PathLossText);
        Assert.Equal("3,00 dB", q.WallLossText);
        Assert.Equal("-38,75 dBm", q.ReceivedText);
        Assert.Equal("Verde oscuro (≥ −50 dBm)", q.LevelText);
        Assert.Equal("#008000", q.LevelColorHex);
        Assert.Equal("Muros cruzados: 1", q.CrossingsTitle);
        CrossingViewModel crossing = Assert.Single(q.Crossings);
        Assert.Equal("B1", crossing.Handle);
        Assert.Equal("Yeso", crossing.Material);
        Assert.Equal("3,0 dB", crossing.LossText);
        Assert.Equal(s.Drawing.Query(new Vec2(8, 4)).Value!.Explanation, q.Explanation);
    }

    [Fact]
    public async Task Query_stays_active_until_cancelled_and_keeps_the_last_point()
    {
        Sample s = Sample.RoomWithAccessPoint();
        s.Interaction.Points.Enqueue(new Vec2(8, 4));
        s.Interaction.Points.Enqueue(new Vec2(3, 4));

        await s.Panel.Query.QueryAsync();

        Assert.Equal(3, s.Interaction.Prompts.Count);
        Assert.Equal("(3; 4) m", s.Panel.Query.PointText);
        Assert.Equal("Muros cruzados: ninguno", s.Panel.Query.CrossingsTitle);
        Assert.False(s.Panel.Query.HasCrossings);
        Assert.Equal(StatusKind.Success, s.Panel.Messages.Kind);
    }

    [Fact]
    public async Task Cancelling_the_first_point_reports_it()
    {
        Sample s = Sample.RoomWithAccessPoint();

        await s.Panel.Query.QueryAsync();

        Assert.False(s.Panel.Query.HasResult);
        Assert.Equal("Consulta cancelada", s.Panel.Messages.Current!.Title);
    }

    [Fact]
    public async Task Query_without_access_point_explains_the_error()
    {
        Sample s = Sample.Room();
        s.Interaction.Points.Enqueue(new Vec2(8, 4));
        s.Interaction.Points.Enqueue(new Vec2(3, 4));

        await s.Panel.Query.QueryAsync();

        Assert.False(s.Panel.Query.HasResult);
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
        Assert.Single(s.Interaction.Prompts);
    }

    [Fact]
    public async Task Walls_crossed_by_the_last_query_are_marked_in_the_table()
    {
        Sample s = Sample.RoomWithAccessPoint();
        s.Interaction.Points.Enqueue(new Vec2(8, 4));

        await s.Panel.Query.QueryAsync();

        Assert.True(s.Row("B1").IsOnPath);
        Assert.Contains("suma 3,0 dB", s.Row("B1").PathNote);
        Assert.False(s.Row("A1").IsOnPath);
        Assert.Equal("", s.Row("A1").PathNote);
    }

    [Fact]
    public async Task Walls_in_a_junction_are_marked_as_not_adding()
    {
        Sample s = Sample.RoomWithAccessPoint();
        s.Interaction.Points.Enqueue(new Vec2(5, 8));

        await s.Panel.Query.QueryAsync();

        CrossingViewModel crossing = Assert.Single(s.Panel.Query.Crossings);
        Assert.Equal("A3", crossing.Handle);
        Assert.True(crossing.HasJunction);
        Assert.Contains("B1", crossing.JunctionNote);
        Assert.True(s.Row("A3").IsOnPath);
        Assert.True(s.Row("B1").IsOnPath);
        Assert.Contains("no suma", s.Row("B1").PathNote);
    }

    [Fact]
    public async Task The_card_follows_changes_in_walls_and_parameters()
    {
        Sample s = Sample.RoomWithAccessPoint();
        s.Interaction.Points.Enqueue(new Vec2(8, 4));
        await s.Panel.Query.QueryAsync();

        s.Row("B1").Material = s.Panel.Materials.Single(m => m.Id == "brick");
        Assert.Equal("8,00 dB", s.Panel.Query.WallLossText);
        Assert.Contains("suma 8,0 dB", s.Row("B1").PathNote);

        s.Panel.AccessPoint.TxPowerText = "10";
        Assert.Equal("-53,75 dBm", s.Panel.Query.ReceivedText);
    }

    [Fact]
    public async Task The_card_is_cleared_when_the_plan_stops_being_valid()
    {
        Sample s = Sample.RoomWithAccessPoint();
        s.Interaction.Points.Enqueue(new Vec2(8, 4));
        await s.Panel.Query.QueryAsync();
        s.Drawing.Add("C9", 2, 2, 3, 2);

        s.Panel.Refresh();

        Assert.False(s.Panel.Query.HasResult);
        Assert.False(s.Row("B1").IsOnPath);
    }

    [Fact]
    public async Task Clear_removes_the_card_and_the_marks()
    {
        Sample s = Sample.RoomWithAccessPoint();
        s.Interaction.Points.Enqueue(new Vec2(8, 4));
        await s.Panel.Query.QueryAsync();

        s.Panel.Query.Clear();
        s.Panel.Refresh();

        Assert.False(s.Panel.Query.HasResult);
        Assert.False(s.Row("B1").IsOnPath);
    }

    [Fact]
    public async Task Show_walls_selects_the_crossed_walls_without_zoom()
    {
        Sample s = Sample.RoomWithAccessPoint();
        s.Interaction.Points.Enqueue(new Vec2(5, 8));
        await s.Panel.Query.QueryAsync();

        s.Panel.Query.ShowWalls();

        (string[] handles, bool zoom) = Assert.Single(s.Interaction.Shown);
        Assert.Equal(["A3", "B1"], handles.Order());
        Assert.False(zoom);
    }

    [Fact]
    public void Export_without_a_map_asks_to_calculate_first()
    {
        Sample s = Sample.RoomWithAccessPoint();

        s.Panel.Export.Export();

        Assert.Empty(s.Interaction.SuggestedPaths);
        Assert.False(s.Panel.Export.HasPath);
        Assert.Equal("Primero calcular el mapa.", s.Panel.Messages.Current!.Detail);
    }

    [Fact]
    public async Task Export_with_an_outdated_map_asks_to_recalculate()
    {
        Sample s = await Sample.CalculatedAsync();
        s.Panel.AccessPoint.TxPowerText = "10";

        s.Panel.Export.Export();

        Assert.Empty(s.Drawing.Exported);
        Assert.Contains("desactualizado", s.Panel.Messages.Text);
    }

    [Fact]
    public async Task Export_writes_the_png_and_shows_its_path()
    {
        Sample s = await Sample.CalculatedAsync();
        s.Interaction.SavePath = Path.Combine(Path.GetTempPath(), "mapa");

        s.Panel.Export.Export();

        string expected = Path.Combine(Path.GetTempPath(), "mapa.png");
        Assert.Equal([s.Drawing.ExportPath], s.Interaction.SuggestedPaths);
        Assert.Equal([expected], s.Drawing.Exported);
        Assert.Equal(expected, s.Panel.Export.LastPath);
        Assert.True(s.Panel.Export.HasPath);
        Assert.Equal(StatusKind.Success, s.Panel.Messages.Kind);
    }

    [Fact]
    public async Task Cancelling_the_file_dialog_exports_nothing()
    {
        Sample s = await Sample.CalculatedAsync();

        s.Panel.Export.Export();

        Assert.Empty(s.Drawing.Exported);
        Assert.Equal("Exportación cancelada", s.Panel.Messages.Current!.Title);
    }

    [Fact]
    public async Task Export_failure_is_shown_as_a_message()
    {
        Sample s = await Sample.CalculatedAsync();
        s.Interaction.SavePath = Path.Combine(Path.GetTempPath(), "mapa.png");
        s.Drawing.ExportFailure = new IOException("disco lleno");

        s.Panel.Export.Export();

        Assert.False(s.Panel.Export.HasPath);
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
        Assert.Contains("disco lleno", s.Panel.Messages.Text);
    }

    [Fact]
    public void Panel_exposes_the_document_name_and_starts_without_message()
    {
        Sample s = Sample.Room();

        Assert.Equal("casa.dwg", s.Panel.DocumentName);
        Assert.Equal("", s.Panel.Messages.Text);
        Assert.False(s.Panel.IsBusy);
        Assert.False(s.Panel.IsInteracting);
    }

    [Fact]
    public void Refresh_is_skipped_while_the_drawing_is_busy()
    {
        Sample s = Sample.Room();
        s.Drawing.Add("C9", 2, 2, 3, 2);
        s.Interaction.BusyReason = "AutoCAD está ejecutando LINE.";

        s.Panel.Refresh();

        Assert.Equal(5, s.Panel.Walls.Rows.Count);
        Assert.Equal("", s.Panel.Messages.Text);
    }

    [Fact]
    public async Task Actions_are_refused_while_the_drawing_is_busy()
    {
        Sample s = Sample.RoomWithAccessPoint();
        s.Interaction.BusyReason = "AutoCAD está ejecutando LINE.";
        s.Interaction.Points.Enqueue(new Vec2(8, 4));

        await s.Panel.Query.QueryAsync();
        await s.Panel.Calculation.CalculateAsync();

        Assert.Empty(s.Interaction.Prompts);
        Assert.Equal(0, s.Drawing.Draws);
        Assert.Equal("AutoCAD está ejecutando LINE.", s.Panel.Messages.Current!.Detail);
    }

    [Fact]
    public void An_unexpected_failure_becomes_an_error_message()
    {
        Sample s = Sample.Room();
        s.Interaction.ShowFailure = new InvalidOperationException("sin vista");

        s.Panel.Walls.ShowRow(s.Row("A1"));

        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
        Assert.Contains("sin vista", s.Panel.Messages.Text);
    }

    [Fact]
    public async Task A_second_action_while_waiting_for_the_drawing_is_refused()
    {
        Sample s = Sample.RoomWithAccessPoint();
        var pending = new TaskCompletionSource<Vec2?>();
        var interaction = new PendingPointInteraction(pending.Task);
        var panel = new PanelViewModel(s.Drawing, interaction);
        panel.Refresh();

        Task query = panel.Query.QueryAsync();
        Assert.True(panel.IsInteracting);
        Assert.True(panel.Query.IsActive);
        Assert.Equal(PanelMode.QueryPoint, panel.ActiveMode);
        panel.Validation.Validate();
        Assert.Equal(StatusKind.Warning, panel.Messages.Kind);

        pending.SetResult(null);
        await query;

        Assert.False(panel.IsInteracting);
        Assert.False(panel.Query.IsActive);
    }

    [Fact]
    public void ViewModels_do_not_use_AutoCAD_or_control_types()
    {
        string[] forbidden = ["Autodesk", "Ac", "Ad", "PresentationFramework", "PresentationCore", "WindowsBase", "System.Windows.Forms", "System.Xaml"];
        Type[] types;
        try
        {
            types = typeof(PanelViewModel).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = [.. ex.Types.OfType<Type>()];
        }

        const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        var used = new HashSet<string>();
        Type[] viewModels = [.. types.Where(t => t.Namespace == typeof(PanelViewModel).Namespace)];
        foreach (Type type in viewModels)
        {
            foreach (FieldInfo f in type.GetFields(all)) Collect(f.FieldType);
            foreach (PropertyInfo p in type.GetProperties(all)) Collect(p.PropertyType);
            foreach (MethodInfo m in type.GetMethods(all))
            {
                Collect(m.ReturnType);
                foreach (ParameterInfo p in m.GetParameters()) Collect(p.ParameterType);
            }

            if (type.BaseType is not null) Collect(type.BaseType);
            foreach (Type i in type.GetInterfaces()) Collect(i);
        }

        Assert.True(viewModels.Length >= 16);
        Assert.DoesNotContain(used, name => forbidden.Any(prefix => name == prefix || name.StartsWith(prefix + ".", StringComparison.Ordinal) || (prefix.Length == 2 && name.StartsWith(prefix, StringComparison.Ordinal) && char.IsUpper(name[2]))));

        void Collect(Type t)
        {
            used.Add(t.Assembly.GetName().Name!);
            foreach (Type argument in t.GetGenericArguments()) Collect(argument);
            if (t.HasElementType) Collect(t.GetElementType()!);
        }
    }

    private sealed class PendingPointInteraction(Task<Vec2?> point) : IDrawingInteraction
    {
        public string? BusyReason => null;
        public Task<Vec2?> PickPointAsync(string prompt) => point;
        public Task<BoundingBox?> PickWindowAsync(string prompt) => Task.FromResult<BoundingBox?>(null);
        public Task<IReadOnlyList<string>?> PickEntitiesAsync(string prompt) => Task.FromResult<IReadOnlyList<string>?>(null);
        public Task<string?> PickPolylineAsync(string prompt) => Task.FromResult<string?>(null);
        public void CancelPrompt() => Cancelled++;
        public int Cancelled { get; private set; }
        public void Show(IReadOnlyCollection<string> handles, bool zoom) { }
        public string? AskSavePath(string suggestedPath) => null;
        public Task YieldAsync() => Task.CompletedTask;
    }
}
