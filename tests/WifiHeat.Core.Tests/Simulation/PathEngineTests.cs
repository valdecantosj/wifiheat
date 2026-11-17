using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Tests.Data;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class PathEngineTests
{
    public static TheoryData<string> SceneNames => [.. ReferenceData.Scenes.Select(s => s.Name)];

    private static PathEngine EngineOf(ReferenceScene reference, PropagationOptions? options = null) =>
        new(reference.Scene(), reference.AccessPoint, reference.Settings, options);

    [Fact]
    public void The_reference_was_generated_with_the_default_options()
    {
        Assert.Equal(ReferenceData.MaxDiffractions, PropagationOptions.Default.MaxDiffractions);
        Assert.Equal(0.02, ReferenceData.ToleranceDb);
        Assert.Equal(20, ReferenceData.TxPowerDbm);
    }

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void Straight_line_matches_the_reference(string name)
    {
        ReferenceScene reference = ReferenceData.Scene(name);
        SimulationScene scene = reference.Scene();

        foreach (ReferencePoint expected in reference.Points)
        {
            double rssi = PointCalculator.ReceivedPowerDbm(scene, reference.AccessPoint, reference.Settings, expected.Point);
            Assert.Equal(expected.StraightLine, rssi, 0.02);
        }
    }

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void Diffraction_matches_the_reference(string name)
    {
        ReferenceScene reference = ReferenceData.Scene(name);
        PathEngine engine = EngineOf(reference);

        foreach (ReferencePoint expected in reference.Points)
        {
            Assert.Equal(expected.Diffraction, engine.ReceivedPowerDbm(expected.Point), 0.02);
        }
    }

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void The_result_does_not_depend_on_the_order_of_the_obstacles(string name)
    {
        ReferenceScene reference = ReferenceData.Scene(name);
        var reversed = new PathEngine(new SimulationScene([.. reference.Walls.Reverse()], ReferenceData.Catalog), reference.AccessPoint, reference.Settings);

        foreach (ReferencePoint expected in reference.Points)
        {
            Assert.Equal(expected.Diffraction, reversed.ReceivedPowerDbm(expected.Point), 0.02);
        }
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(1)]
    [InlineData(3.3)]
    [InlineData(6)]
    public void The_signal_is_continuous_across_the_shadow_of_a_column(double x)
    {
        PathEngine engine = EngineOf(ReferenceData.Scene("columna-sola"));

        double previous = engine.ReceivedPowerDbm(new Vec2(x, -1.5));
        for (int i = 1; i <= 660; i++)
        {
            double y = -1.5 + i * 0.005;
            double rssi = engine.ReceivedPowerDbm(new Vec2(x, y));
            Assert.True(Math.Abs(rssi - previous) <= 1.0, $"Salto de {rssi - previous:0.00} dB en y = {y:0.000}");
            previous = rssi;
        }
    }

    [Theory]
    [InlineData("mixta")]
    [InlineData("columnas-en-fila")]
    [InlineData("recinto")]
    public void Parallel_queries_give_the_same_values_as_sequential_ones(string name)
    {
        ReferenceScene reference = ReferenceData.Scene(name);
        PathEngine engine = EngineOf(reference);
        Vec2[] points = [.. Enumerable.Range(0, 900).Select(i => new Vec2(-2.05 + 0.4 * (i % 30), -3.95 + 0.33 * (i / 30)))];

        double[] sequential = [.. points.Select(engine.ReceivedPowerDbm)];
        var parallel = new double[points.Length];
        Parallel.For(0, points.Length, i => parallel[i] = engine.ReceivedPowerDbm(points[i]));

        Assert.Equal(sequential, parallel);
        Assert.All(sequential, value => Assert.True(double.IsFinite(value)));
    }

    [Fact]
    public void Without_obstacles_only_the_direct_path_remains()
    {
        ReferenceScene reference = ReferenceData.Scene("columna-sola");
        var engine = new PathEngine(new SimulationScene([], ReferenceData.Catalog), reference.AccessPoint, reference.Settings);
        var point = new Vec2(3.3, 0.15);

        PathContribution path = Assert.Single(engine.Paths(point));

        Assert.Empty(engine.Vertices);
        Assert.Equal(PathKind.Direct, path.Kind);
        Assert.Equal(6.3, path.LengthM, 9);
        Assert.Equal(1, path.PowerShare, 12);
        Assert.Equal(PathLoss.FreeSpaceDbFromMetres(5180, 6.3), path.LossDb, 9);
        Assert.Equal(20 - path.LossDb, engine.ReceivedPowerDbm(point), 9);
    }

    [Fact]
    public void Far_from_the_shadow_the_result_is_the_straight_line_one()
    {
        ReferenceScene reference = ReferenceData.Scene("columna-sola");
        PathEngine engine = EngineOf(reference);
        var point = new Vec2(-3, 4);

        double straight = PointCalculator.ReceivedPowerDbm(reference.Scene(), reference.AccessPoint, reference.Settings, point);

        Assert.Equal(straight, engine.ReceivedPowerDbm(point), 0.01);
    }

    [Fact]
    public void Behind_a_column_the_paths_around_its_corners_carry_the_power()
    {
        PathEngine engine = EngineOf(ReferenceData.Scene("columna-sola"));
        var point = new Vec2(3.3, 0.15);

        IReadOnlyList<PathContribution> paths = engine.Paths(point);

        Assert.Equal(2, paths.Count);
        Assert.All(paths, p => Assert.Equal(PathKind.Diffracted, p.Kind));
        Assert.All(paths, p => Assert.Equal(2, p.Diffractions));
        Assert.All(paths, p => Assert.Equal(0.5, p.PowerShare, 6));
        Assert.Contains(paths, p => p.Vertices.SequenceEqual([new Vec2(0, 0), new Vec2(0.3, 0)]));
        Assert.Contains(paths, p => p.Vertices.SequenceEqual([new Vec2(0, 0.3), new Vec2(0.3, 0.3)]));
        Assert.Equal(1, paths.Sum(p => p.PowerShare), 9);
        Assert.Equal(paths.OrderBy(p => p.LossDb).Select(p => p.LossDb), paths.Select(p => p.LossDb));
        Assert.Equal(20 + 10 * Math.Log10(paths.Sum(p => Math.Pow(10, -p.LossDb / 10))), engine.ReceivedPowerDbm(point), 9);
        Assert.All(paths, p => Assert.True(p.LengthM > 6.3));
    }

    [Fact]
    public void Paths_weaker_than_the_best_by_the_prune_margin_are_left_out()
    {
        ReferenceScene reference = ReferenceData.Scene("columna-sola");
        var point = new Vec2(3.3, 0.15);

        IReadOnlyList<PathContribution> pruned = EngineOf(reference).Paths(point);
        IReadOnlyList<PathContribution> all = EngineOf(reference, new PropagationOptions(PruneDb: 500)).Paths(point);

        Assert.True(all.Count > pruned.Count);
        Assert.Contains(all, p => p.Kind == PathKind.Direct);
        Assert.DoesNotContain(pruned, p => p.Kind == PathKind.Direct);
        Assert.All(pruned, p => Assert.True(p.LossDb <= pruned[0].LossDb + 30));
    }

    [Fact]
    public void Fewer_diffractions_never_give_more_signal_behind_obstacles_in_a_row()
    {
        ReferenceScene reference = ReferenceData.Scene("columnas-en-fila");
        var point = new Vec2(7, 0.15);

        double one = EngineOf(reference, new PropagationOptions(MaxDiffractions: 1)).ReceivedPowerDbm(point);
        double two = EngineOf(reference, new PropagationOptions(MaxDiffractions: 2)).ReceivedPowerDbm(point);
        double three = EngineOf(reference).ReceivedPowerDbm(point);
        double four = EngineOf(reference, new PropagationOptions(MaxDiffractions: 4)).ReceivedPowerDbm(point);

        Assert.True(one <= two);
        Assert.True(two <= three);
        Assert.True(three <= four);
        Assert.True(three > one + 10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    public void The_number_of_diffractions_goes_from_one_to_four(int maxDiffractions)
    {
        ReferenceScene reference = ReferenceData.Scene("columna-sola");

        Assert.Throws<ArgumentOutOfRangeException>(() => EngineOf(reference, new PropagationOptions(MaxDiffractions: maxDiffractions)));
    }

    [Fact]
    public void The_link_budget_terms_shift_the_result()
    {
        ReferenceScene reference = ReferenceData.Scene("esquina");
        var point = new Vec2(3, 2);
        RadioSettings settings = reference.Settings with { TxPowerDbm = 17, TxAntennaGainDbi = 3, RxAntennaGainDbi = 2, TxCableLossDb = 1, RxCableLossDb = 0.5 };

        double plain = EngineOf(reference).ReceivedPowerDbm(point);
        double adjusted = new PathEngine(reference.Scene(), reference.AccessPoint, settings).ReceivedPowerDbm(point);

        Assert.Equal(plain - 3 + 3 + 2 - 1 - 0.5, adjusted, 9);
    }

    [Fact]
    public void A_nearly_transparent_edge_hardly_diffracts()
    {
        var catalog = new MaterialCatalog([new Material("film", "Lámina", 0.01, 0.01)]);
        var wall = new Wall("F", [new Segment2(new Vec2(0, 0), new Vec2(0, 50))], "film", 0.001);
        var settings = new RadioSettings { Band = Band.Ghz5, TxPowerDbm = 20 };
        var ap = new AccessPoint(new Vec2(-3, 2));
        var scene = new SimulationScene([wall], catalog);
        var engine = new PathEngine(scene, ap, settings);

        foreach (double y in new[] { 2.0, 0.05, -0.05, -2.0 })
        {
            var point = new Vec2(3, y);
            double freeSpace = 20 - PathLoss.FreeSpaceDbFromMetres(5180, Vec2.Distance(ap.Position, point));
            Assert.Equal(freeSpace, engine.ReceivedPowerDbm(point), 0.1);
        }
    }
}
