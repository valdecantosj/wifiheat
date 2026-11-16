using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Tests.Service;

namespace WifiHeat.AutoCAD.UI.Tests;

internal sealed class FakeInteraction : IDrawingInteraction
{
    public string? BusyReason { get; set; }
    public Queue<Vec2> Points { get; } = new();
    public BoundingBox? Window { get; set; }
    public IReadOnlyList<string>? Entities { get; set; }
    public string? Polyline { get; set; }
    public string? SavePath { get; set; }
    public Exception? ShowFailure { get; set; }
    public Action? OnYield { get; set; }
    public List<(string[] Handles, bool Zoom)> Shown { get; } = [];
    public List<string> Prompts { get; } = [];
    public List<string> SuggestedPaths { get; } = [];

    public Task<Vec2?> PickPointAsync(string prompt)
    {
        Prompts.Add(prompt);
        return Task.FromResult(Points.Count > 0 ? Points.Dequeue() : (Vec2?)null);
    }

    public Task<BoundingBox?> PickWindowAsync(string prompt)
    {
        Prompts.Add(prompt);
        return Task.FromResult(Window);
    }

    public Task<IReadOnlyList<string>?> PickEntitiesAsync(string prompt)
    {
        Prompts.Add(prompt);
        return Task.FromResult(Entities);
    }

    public Task<string?> PickPolylineAsync(string prompt)
    {
        Prompts.Add(prompt);
        return Task.FromResult(Polyline);
    }

    public int Cancelled { get; private set; }

    public void CancelPrompt() => Cancelled++;

    public void Show(IReadOnlyCollection<string> handles, bool zoom)
    {
        if (ShowFailure is not null) throw ShowFailure;
        Shown.Add(([.. handles], zoom));
    }

    public string? AskSavePath(string suggestedPath)
    {
        SuggestedPaths.Add(suggestedPath);
        return SavePath;
    }

    public Task YieldAsync()
    {
        OnYield?.Invoke();
        return Task.CompletedTask;
    }
}

internal sealed class Sample
{
    public FakeDrawing Drawing { get; } = new();
    public FakeInteraction Interaction { get; } = new();
    public PanelViewModel Panel { get; }

    private Sample(Action<FakeDrawing> arrange)
    {
        arrange(Drawing);
        Panel = new PanelViewModel(Drawing, Interaction, "casa.dwg");
        Panel.Refresh();
    }

    public static Sample Empty() => new(_ => { });

    public static Sample Custom(Action<FakeDrawing> arrange) => new(arrange);

    public static Sample Room(string? material = "brick") => new(d => AddRoom(d, material));

    public static Sample RoomWithAccessPoint()
    {
        Sample s = Room();
        s.Drawing.SetAccessPoint(new Vec2(2, 4));
        s.Panel.Refresh();
        return s;
    }

    public static async Task<Sample> CalculatedAsync()
    {
        Sample s = RoomWithAccessPoint();
        await s.Panel.Calculation.CalculateAsync();
        return s;
    }

    public WallRowViewModel Row(string handle) => Panel.Walls.Rows.Single(r => r.Handle == handle);

    private static void AddRoom(FakeDrawing d, string? material)
    {
        d.Add("A1", 0, 0, 10, 0, material, 0.2);
        d.Add("A2", 10, 0, 10, 8, material, 0.2);
        d.Add("A3", 10, 8, 0, 8, material, 0.2);
        d.Add("A4", 0, 8, 0, 0, material, 0.2);
        d.Add("B1", 5, 0, 5, 8, material is null ? null : "gypsum", 0.1);
    }
}
