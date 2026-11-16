using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.Core.Geometry;
using Xunit;

namespace WifiHeat.AutoCAD.UI.Tests;

public class MessagesAndModeTests
{
    [Theory]
    [InlineData(StatusKind.Error, "✖", true)]
    [InlineData(StatusKind.Warning, "⚠", true)]
    [InlineData(StatusKind.Success, "✔", false)]
    [InlineData(StatusKind.Info, "ℹ", false)]
    public void Each_severity_has_its_icon_and_only_problems_can_be_dismissed(StatusKind kind, string icon, bool canDismiss)
    {
        var messages = new MessageCenter();

        messages.Post(kind, " Título ", " Detalle ");

        PanelMessage current = messages.Current!;
        Assert.Equal(kind, messages.Kind);
        Assert.Equal(icon, current.Icon);
        Assert.Equal(canDismiss, current.CanDismiss);
        Assert.Equal("Título", current.Title);
        Assert.Equal("Detalle", current.Detail);
        Assert.NotEqual("", current.KindName);
    }

    [Fact]
    public void Dismiss_closes_errors_but_not_successes()
    {
        var messages = new MessageCenter();

        messages.Post(StatusKind.Success, "Listo");
        messages.DismissCommand.Execute(null);
        Assert.True(messages.HasCurrent);

        messages.Post(StatusKind.Error, "Falló", "Motivo.");
        messages.DismissCommand.Execute(null);
        Assert.False(messages.HasCurrent);
        Assert.Equal(2, messages.History.Count);
    }

    [Fact]
    public void An_error_stays_through_automatic_refreshes_and_goes_away_with_the_next_action()
    {
        Sample s = Sample.Room();
        s.Row("A1").ThicknessText = "x";
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);

        s.Drawing.Add("C9", 2, 2, 3, 2, "brick", 0.2);
        s.Panel.Refresh();
        Assert.Equal(StatusKind.Error, s.Panel.Messages.Kind);
        Assert.Equal("Espesor no válido", s.Panel.Messages.Current!.Title);

        s.Panel.Walls.ShowRow(s.Row("A1"));
        Assert.False(s.Panel.Messages.HasCurrent);
    }

    [Fact]
    public void A_success_lasts_until_the_next_action()
    {
        Sample s = Sample.Room();
        s.Panel.Validation.Validate();
        Assert.Equal(StatusKind.Success, s.Panel.Messages.Kind);
        Assert.Equal("Plano válido", s.Panel.Messages.Current!.Title);

        s.Panel.Refresh();
        Assert.True(s.Panel.Messages.HasCurrent);

        s.Panel.Walls.ShowRow(s.Row("A1"));
        Assert.False(s.Panel.Messages.HasCurrent);
    }

    [Fact]
    public void History_keeps_the_last_fifty_newest_first_and_can_be_cleared()
    {
        var time = new DateTime(2026, 11, 15, 10, 0, 0);
        var messages = new MessageCenter(() => time = time.AddSeconds(1));

        for (int i = 1; i <= 60; i++) messages.Post(StatusKind.Info, $"Mensaje {i}", i % 2 == 0 ? "Con detalle." : "");

        Assert.Equal(MessageCenter.HistoryLimit, messages.History.Count);
        Assert.Equal("50 mensajes", messages.HistorySummary);
        Assert.Equal("Mensaje 60", messages.History[0].Title);
        Assert.Equal("Mensaje 11", messages.History[^1].Title);
        Assert.Equal("10:01:00", messages.History[0].TimeText);
        Assert.StartsWith("10:01:00 ℹ Mensaje 60 — Con detalle.", messages.HistoryText);

        messages.ClearHistoryCommand.Execute(null);

        Assert.Empty(messages.History);
        Assert.False(messages.HasHistory);
        Assert.Equal("", messages.HistoryText);
        Assert.True(messages.HasCurrent);
    }

    [Fact]
    public void The_same_message_twice_is_shown_again_and_kept_twice()
    {
        var messages = new MessageCenter(() => new DateTime(2026, 11, 15));

        messages.Post(StatusKind.Error, "Falló");
        messages.Dismiss();
        messages.Post(StatusKind.Error, "Falló");

        Assert.True(messages.HasCurrent);
        Assert.Equal(2, messages.History.Count);
    }

    [Fact]
    public async Task Warnings_keep_the_success_title_without_a_repeated_prefix()
    {
        Sample s = Sample.Custom(d =>
        {
            d.AddFace("muros", "F1", 0, 0, 4, 0);
            d.AddFace("muros", "F2", 0, 0.2, 4, 0.2);
            d.AddFace("muros", "F3", 6, 3, 8, 3);
        });
        s.Interaction.Window = new BoundingBox(new Vec2(-1, -1), new Vec2(10, 5));

        await s.Panel.Prepare.PickWindowAsync();
        s.Panel.Prepare.GenerateAxes();

        PanelMessage message = s.Panel.Messages.Current!;
        Assert.DoesNotContain("Aviso", message.Text);
        Assert.DoesNotContain(message.Title, message.Detail);
        Assert.Contains("eje(s)", message.Title);
    }

    [Fact]
    public async Task The_mode_is_shown_while_waiting_and_cleared_when_cancelled()
    {
        Sample s = Sample.RoomWithAccessPoint();
        var interaction = new ControlledInteraction();
        var panel = new PanelViewModel(s.Drawing, interaction);
        panel.Refresh();

        Task pick = panel.AccessPoint.PickAsync();

        Assert.Equal(PanelMode.PlaceAccessPoint, panel.ActiveMode);
        Assert.True(panel.IsInteracting);
        Assert.Equal("MODO: UBICAR PUNTO DE ACCESO", panel.ActiveMode!.Title);

        panel.CancelModeCommand.Execute(null);
        await pick;

        Assert.Equal(1, interaction.Cancelled);
        Assert.Null(panel.ActiveMode);
        Assert.False(panel.IsInteracting);
        Assert.Equal(StatusKind.Info, panel.Messages.Kind);
    }

    [Fact]
    public async Task The_mode_is_cleared_when_the_drawing_fails()
    {
        Sample s = Sample.RoomWithAccessPoint();
        var interaction = new ControlledInteraction();
        var panel = new PanelViewModel(s.Drawing, interaction);
        panel.Refresh();

        Task query = panel.Query.QueryAsync();
        Assert.Equal(PanelMode.QueryPoint, panel.ActiveMode);

        interaction.Fail(new InvalidOperationException("editor ocupado"));
        await query;

        Assert.Null(panel.ActiveMode);
        Assert.False(panel.Query.IsActive);
        Assert.Equal(StatusKind.Error, panel.Messages.Kind);
        Assert.Contains("editor ocupado", panel.Messages.Current!.Detail);
    }

    [Fact]
    public async Task Every_drawing_question_has_its_own_mode()
    {
        Sample s = Sample.RoomWithAccessPoint();
        var interaction = new ControlledInteraction();
        var panel = new PanelViewModel(s.Drawing, interaction);
        panel.Refresh();
        var seen = new List<PanelMode>();
        panel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(PanelViewModel.ActiveMode) && panel.ActiveMode is { } mode) seen.Add(mode);
        };

        foreach (Func<Task> action in new Func<Task>[]
                 {
                     panel.Prepare.PickWindowAsync, panel.Walls.PickInDrawingAsync, panel.AccessPoint.PickAsync,
                     panel.Query.QueryAsync, panel.Calculation.PickAreaAsync,
                 })
        {
            Task running = action();
            panel.CancelModeCommand.Execute(null);
            await running;
            Assert.Null(panel.ActiveMode);
        }

        Assert.Equal([PanelMode.PickZone, PanelMode.PickLines, PanelMode.PlaceAccessPoint, PanelMode.QueryPoint, PanelMode.CoverageArea], seen);
        Assert.Equal(5, seen.Select(m => m.Title).Distinct().Count());
    }

    [Fact]
    public void Cancel_does_nothing_without_an_active_mode()
    {
        Sample s = Sample.Room();

        s.Panel.CancelModeCommand.Execute(null);

        Assert.Equal(0, s.Interaction.Cancelled);
    }

    private sealed class ControlledInteraction : IDrawingInteraction
    {
        private TaskCompletionSource<bool>? _pending;

        public int Cancelled { get; private set; }

        public string? BusyReason => null;

        public Task<Vec2?> PickPointAsync(string prompt) => Wait<Vec2?>(new Vec2(1, 1));

        public Task<BoundingBox?> PickWindowAsync(string prompt) => Wait<BoundingBox?>(new BoundingBox(new Vec2(0, 0), new Vec2(1, 1)));

        public Task<IReadOnlyList<string>?> PickEntitiesAsync(string prompt, bool includeHatches = false) => Wait<IReadOnlyList<string>?>(["A1"]);

        public Task<string?> PickPolylineAsync(string prompt) => Wait<string?>("P1");

        public void CancelPrompt()
        {
            Cancelled++;
            _pending?.TrySetResult(false);
        }

        public void Fail(Exception ex) => _pending?.TrySetException(ex);

        public void Show(IReadOnlyCollection<string> handles, bool zoom)
        {
        }

        public string? AskSavePath(string suggestedPath) => null;

        public Task YieldAsync() => Task.CompletedTask;

        private async Task<T?> Wait<T>(T answer)
        {
            _pending = new TaskCompletionSource<bool>();
            bool answered = await _pending.Task;
            return answered ? answer : default;
        }
    }
}
