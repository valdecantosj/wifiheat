using System.Text.Json;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;

namespace WifiHeat.Core.Tests.Data;

public sealed record ReferencePoint(Vec2 Point, double StraightLine, double Diffraction);

public sealed record ReferenceVertex(Vec2 Position, double FreeSectorDeg, double LossDb);

public sealed record ReferenceScene(
    string Name,
    Band Band,
    Vec2 Ap,
    IReadOnlyList<Wall> Walls,
    IReadOnlyList<ReferencePoint> Points,
    IReadOnlyList<ReferenceVertex> Vertices)
{
    public SimulationScene Scene() => new(Walls, ReferenceData.Catalog);

    public AccessPoint AccessPoint => new(Ap);

    public RadioSettings Settings => new() { Band = Band, TxPowerDbm = ReferenceData.TxPowerDbm };
}

public static class ReferenceData
{
    private static readonly Lazy<JsonDocument> Document = new(() =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "propagation-reference.json"))));

    private static readonly Lazy<IReadOnlyList<ReferenceScene>> AllScenes = new(() =>
        [.. Document.Value.RootElement.GetProperty("scenes").EnumerateArray().Select(ReadScene)]);

    public static MaterialCatalog Catalog { get; } =
        MaterialCatalogParser.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "materials.json")));

    public static double TxPowerDbm => Document.Value.RootElement.GetProperty("txPowerDbm").GetDouble();

    public static double ToleranceDb => Document.Value.RootElement.GetProperty("toleranceDb").GetDouble();

    public static int MaxDiffractions => Document.Value.RootElement.GetProperty("maxDiffractions").GetInt32();

    public static IReadOnlyList<ReferenceScene> Scenes => AllScenes.Value;

    public static IReadOnlyList<(double Nu, double LossDb)> KnifeEdge =>
        [.. Document.Value.RootElement.GetProperty("knifeEdge").EnumerateArray()
            .Select(e => (e.GetProperty("nu").GetDouble(), e.GetProperty("lossDb").GetDouble()))];

    public static ReferenceScene Scene(string name) => Scenes.Single(s => s.Name == name);

    private static ReferenceScene ReadScene(JsonElement e)
    {
        var walls = new List<Wall>();
        foreach (JsonElement o in e.GetProperty("obstacles").EnumerateArray())
        {
            string id = o.GetProperty("id").GetString()!;
            string material = o.GetProperty("material").GetString()!;
            Vec2[] points = [.. o.GetProperty("points").EnumerateArray().Select(ReadPair)];
            if (o.GetProperty("kind").GetString() == "column")
            {
                ColumnReinforcement.TryParseId(o.GetProperty("reinforcement").GetString(), out Reinforcement level);
                walls.Add(Wall.Column(id, new Polygon2(points), material, level));
            }
            else
            {
                var segments = new List<Segment2>();
                for (int i = 0; i + 1 < points.Length; i++) segments.Add(new Segment2(points[i], points[i + 1]));
                walls.Add(new Wall(id, segments, material, o.GetProperty("thicknessM").GetDouble()));
            }
        }

        return new ReferenceScene(
            e.GetProperty("name").GetString()!,
            e.GetProperty("band").GetString() == "2.4GHz" ? Band.Ghz2_4 : Band.Ghz5,
            ReadPair(e.GetProperty("ap")),
            walls,
            [.. e.GetProperty("points").EnumerateArray().Select(p => new ReferencePoint(
                new Vec2(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble()),
                p.GetProperty("straightLine").GetDouble(),
                p.GetProperty("diffraction").GetDouble()))],
            [.. e.GetProperty("diffractingVertices").EnumerateArray().Select(v => new ReferenceVertex(
                new Vec2(v.GetProperty("x").GetDouble(), v.GetProperty("y").GetDouble()),
                v.GetProperty("freeSectorDeg").GetDouble(),
                v.GetProperty("lossDb").GetDouble()))]);
    }

    private static Vec2 ReadPair(JsonElement e) => new(e[0].GetDouble(), e[1].GetDouble());
}
