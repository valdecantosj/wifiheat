using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Walls;
using WifiHeat.Core.Workflow;

namespace WifiHeat.Core.Tests.Service;

internal sealed class FakeDrawing(MaterialCatalog? catalog = null) : WifiHeatService(new SimulationSession(catalog ?? DefaultCatalog))
{
    public const string WallLayer = "WIFIHEAT_MUROS";

    private int _nextHandle = 1;

    public static MaterialCatalog DefaultCatalog { get; } = new([new Material("brick", "Ladrillo", 8, 12), new Material("gypsum", "Yeso", 3, 4)]);

    public static MaterialCatalog CatalogWithConcrete { get; } = new(
        [new Material("brick", "Ladrillo", 8, 12), new Material("gypsum", "Yeso", 3, 4), new Material("concrete", "Hormigón", 20, 40)]);

    public DrawingUnit? Unit { get; set; } = DrawingUnit.Metres;
    public Dictionary<string, Wall> Walls { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> GeneratedHandles { get; } = new(StringComparer.OrdinalIgnoreCase);
    public const string FaceLayer = "WIFIHEAT_CARAS";

    public Dictionary<string, (string Layer, List<Segment2> Segments)> Lines { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ClosedLines { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ClosedFigure> Figures { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, List<FaceSegment>> Faces { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, Polygon2> Polylines { get; } = new(StringComparer.OrdinalIgnoreCase);
    public List<ValidationIssue> HostIssues { get; } = [];
    public Polygon2? Area { get; set; }
    public string? AreaHandle { get; private set; }
    public List<(AccessPoint Ap, RadioSettings Settings)> SavedAccessPoints { get; } = [];
    public int Draws { get; private set; }
    public List<int> FadesApplied { get; } = [];
    public int LegendRedraws { get; private set; }
    public List<string> Exported { get; } = [];
    public Exception? ExportFailure { get; set; }
    public string ExportPath { get; set; } = "C:/planos/casa-wifiheat.png";

    public override string SuggestedExportPath => ExportPath;

    public void Add(string handle, double ax, double ay, double bx, double by, string? material = null, double? thickness = null) =>
        Walls[handle] = new Wall(handle, [new Segment2(new Vec2(ax, ay), new Vec2(bx, by))], material, thickness);

    public void AddFigure(string handle, string kind, double x, double y, double width, double height) =>
        Figures[handle] = new ClosedFigure(handle, kind, Polygon2.Rectangle(new BoundingBox(new Vec2(x, y), new Vec2(x + width, y + height))), null);

    public void AddClosedPolyline(string layer, string handle, double x, double y, double width, double height)
    {
        Lines[handle] = (layer, [.. Polygon2.Rectangle(new BoundingBox(new Vec2(x, y), new Vec2(x + width, y + height))).Edges()]);
        ClosedLines.Add(handle);
    }

    public void AddBrokenFigure(string handle, string kind, string error) => Figures[handle] = new ClosedFigure(handle, kind, null, error);

    public void AddColumn(string handle, double x, double y, double side, Reinforcement level, string? material = "concrete") =>
        Walls[handle] = Wall.Column(handle, Polygon2.Rectangle(new BoundingBox(new Vec2(x, y), new Vec2(x + side, y + side))), material, level);

    public void AddLine(string layer, string handle, double ax, double ay, double bx, double by) =>
        Lines[handle] = (layer, [new Segment2(new Vec2(ax, ay), new Vec2(bx, by))]);

    public void AddFace(string layer, string id, double ax, double ay, double bx, double by)
    {
        if (!Faces.TryGetValue(layer, out List<FaceSegment>? list)) Faces[layer] = list = [];
        list.Add(new FaceSegment(id, new Segment2(new Vec2(ax, ay), new Vec2(bx, by))));
    }

    protected override DrawingSetup ReadSetup() => new(Unit, DrawingUnit.Millimetres, "INSUNITS=Millimeters");

    protected override void WriteUnit(DrawingUnit unit) => Unit = unit;

    protected override DrawingContents ReadDrawing()
    {
        List<ValidationIssue> issues = [.. HostIssues];
        if (Unit is null) issues.Add(DrawingIssues.MissingUnit());
        return new DrawingContents([.. Walls.Values.Select(w => new DrawingWall(w, WallLayer))], issues);
    }

    protected override CoverageAreaRead ReadCoverageArea() =>
        new(Area, Area is null ? "rectángulo de los muros" : $"polilínea {AreaHandle ?? "?"}", null, Area is null ? null : AreaHandle);

    protected override OperationResult<CoverageAreaRead> WriteCoverageArea(string? polylineHandle)
    {
        if (polylineHandle is null)
        {
            Area = null;
            AreaHandle = null;
            return OperationResult<CoverageAreaRead>.Ok(ReadCoverageArea());
        }

        if (!Polylines.TryGetValue(polylineHandle, out Polygon2? polygon))
        {
            return OperationResult<CoverageAreaRead>.Fail($"La entidad {polylineHandle} no es una polilínea. No se cambió el área.");
        }

        Area = polygon;
        AreaHandle = polylineHandle;
        return OperationResult<CoverageAreaRead>.Ok(ReadCoverageArea());
    }

    protected override IReadOnlyList<LayerFaces> ReadFaces(BoundingBox windowM) =>
        [.. Faces.Select(kv => new LayerFaces(kv.Key, WallAxisDetector.InsideWindow(kv.Value, windowM))).Where(l => l.Faces.Count > 0)];

    protected override IReadOnlyList<GeneratedAxis> ReadGeneratedAxes(BoundingBox windowM) =>
        [.. Walls.Values
            .Where(w => GeneratedHandles.Contains(w.Id) && WallAxisDetector.InsideWindow([new FaceSegment(w.Id, w.Segments[0])], windowM).Count == 1)
            .Select(w => new GeneratedAxis(w.Id, w.Segments[0]))];

    protected override void ReplaceAxes(IReadOnlyList<string> eraseHandles, IReadOnlyList<NewAxis> create)
    {
        foreach (string handle in eraseHandles)
        {
            Walls.Remove(handle);
            GeneratedHandles.Remove(handle);
        }

        foreach (NewAxis axis in create)
        {
            string handle = $"G{_nextHandle++}";
            Walls[handle] = new Wall(handle, [axis.Axis], null, axis.ThicknessM);
            GeneratedHandles.Add(handle);
        }
    }

    protected override IReadOnlyList<EntityFaces> ReadEntityFaces(IReadOnlyCollection<string> handles)
    {
        var result = new List<EntityFaces>();
        foreach (string handle in handles)
        {
            if (Walls.TryGetValue(handle, out Wall? wall)) result.Add(new EntityFaces(wall.Id, WallLayer, true, wall.Segments));
            else if (Lines.TryGetValue(handle, out var line)) result.Add(new EntityFaces(handle, line.Layer, false, line.Segments, ClosedLines.Contains(handle)));
        }

        return result;
    }

    protected override IReadOnlyList<ClosedFigure> ReadClosedFigures(IReadOnlyCollection<string> handles)
    {
        var result = new List<ClosedFigure>();
        foreach (string handle in handles)
        {
            if (Figures.TryGetValue(handle, out ClosedFigure? figure)) result.Add(figure);
            else if (Walls.TryGetValue(handle, out Wall? wall)) result.Add(new ClosedFigure(wall.Id, "LWPOLYLINE", wall.Footprint(), wall.Footprint() is null ? "no está cerrada" : null, wall.IsColumn));
            else if (Lines.ContainsKey(handle)) result.Add(new ClosedFigure(handle, "LINE", null, "no es un hatch ni una polilínea cerrada"));
        }

        return result;
    }

    protected override IReadOnlyList<ClosedFigure> ReadClosedFigures(BoundingBox windowM) =>
        [.. Figures.Values.Where(f => f.Polygon is { } p &&
            p.Bounds.Min.X >= windowM.Min.X && p.Bounds.Min.Y >= windowM.Min.Y && p.Bounds.Max.X <= windowM.Max.X && p.Bounds.Max.Y <= windowM.Max.Y)];

    protected override IReadOnlyList<string> WriteColumns(IReadOnlyList<NewColumn> create, IReadOnlyList<ColumnUpdate> update)
    {
        foreach (ColumnUpdate u in update)
        {
            if (Walls.TryGetValue(u.Handle, out Wall? wall)) Walls[u.Handle] = wall with { MaterialId = u.MaterialId, Reinforcement = u.Reinforcement };
        }

        var handles = new List<string>();
        foreach (NewColumn column in create)
        {
            string handle = $"C{_nextHandle++}";
            Walls[handle] = Wall.Column(handle, column.Footprint, column.MaterialId, column.Reinforcement);
            handles.Add(handle);
        }

        return handles;
    }

    protected override IReadOnlyList<string> CreateAxes(IReadOnlyList<NewAxis> axes)
    {
        var handles = new List<string>();
        foreach (NewAxis axis in axes)
        {
            string handle = $"E{_nextHandle++}";
            Walls[handle] = new Wall(handle, [axis.Axis], axis.MaterialId, axis.ThicknessM);
            handles.Add(handle);
        }

        return handles;
    }

    protected override void RetireFaces(IReadOnlyList<string> handles)
    {
        foreach (string handle in handles)
        {
            if (!Walls.Remove(handle, out Wall? wall)) continue;
            GeneratedHandles.Remove(handle);
            Lines[handle] = (FaceLayer, [.. wall.Segments]);
        }
    }

    protected override IReadOnlyList<string> WriteWalls(IReadOnlyList<WallAssignment> assignments)
    {
        var missing = new List<string>();
        foreach (WallAssignment a in assignments)
        {
            if (Walls.TryGetValue(a.Handle, out Wall? w))
            {
                Walls[a.Handle] = w with { MaterialId = a.MaterialId, ThicknessM = a.ThicknessM };
            }
            else if (Lines.Remove(a.Handle, out var line))
            {
                Walls[a.Handle] = new Wall(a.Handle, line.Segments, a.MaterialId, a.ThicknessM);
            }
            else
            {
                missing.Add(a.Handle);
            }
        }

        return missing;
    }

    protected override string? SaveAccessPoint(AccessPoint ap, RadioSettings settings)
    {
        SavedAccessPoints.Add((ap, settings));
        return null;
    }

    protected override OverlayOutcome DrawResult(RssiGrid grid, HeatmapImage image, Legend legend, int fadePercent)
    {
        Draws++;
        return new OverlayOutcome($"overlay_{Draws:000}.png", []);
    }

    protected override string? ApplyOverlayFade(int percent, RssiGrid? grid, Legend legend)
    {
        FadesApplied.Add(percent);
        if (grid is not null) LegendRedraws++;
        return null;
    }

    protected override void WritePng(string path, IReadOnlyList<Wall> walls, HeatmapImage image, AccessPoint ap, Legend legend, int fadePercent)
    {
        if (ExportFailure is not null) throw ExportFailure;
        Exported.Add(path);
    }
}
