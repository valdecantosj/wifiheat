using System.Numerics;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Tests.Data;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class ReflectionTests
{
    private static readonly Dielectric Lossless = new(4, 0, 0, 0);

    private static readonly MaterialCatalog Catalog = new(
    [
        new Material("mirror", "Espejo", 10, 10, Lossless),
        new Material("concrete", "Hormigón", 22.8, 44.8, Lossless),
        new Material("plain", "Sin datos", 5, 5),
    ]);

    private static readonly RadioSettings Settings = new() { Band = Band.Ghz5, TxPowerDbm = 20 };

    private static Wall Line(string id, double x1, double y1, double x2, double y2, string material = "mirror") =>
        new(id, [new Segment2(new Vec2(x1, y1), new Vec2(x2, y2))], material, 0.15);

    private static PathEngine Engine(IReadOnlyList<Wall> walls, Vec2 ap) =>
        new(new SimulationScene(walls, Catalog), new AccessPoint(ap), Settings, PropagationModel.DiffractionAndReflection);

    private static IReadOnlyList<PathContribution> Reflected(PathEngine engine, Vec2 point) =>
        [.. engine.Paths(point).Where(p => p.Kind == PathKind.Reflected)];

    [Fact]
    public void One_bounce_follows_the_image_method()
    {
        PathEngine engine = Engine([Line("W1", 0, 0, 10, 0)], new Vec2(2, 2));

        PathContribution path = Assert.Single(Reflected(engine, new Vec2(6, 2)));

        Assert.Equal(4, path.Vertices[0].X, 9);
        Assert.Equal(0, path.Vertices[0].Y, 9);
        Assert.Equal(Math.Sqrt(32), path.LengthM, 9);
        Assert.Equal("W1", path.Reflection!.WallId);
        Assert.False(path.Reflection.IsColumn);
        Assert.Equal(45, path.Reflection.IncidenceDeg, 6);

        double bounce = Fresnel.ReflectionLossDb(new Complex(4, 0), Math.Cos(Math.PI / 4));
        Assert.Equal(bounce, path.Reflection.LossDb, 9);
        Assert.Equal(PathLoss.FreeSpaceDbFromMetres(Settings.FrequencyMHz, Math.Sqrt(32)) + bounce, path.LossDb, 9);
    }

    [Fact]
    public void The_bounce_adds_power_to_the_direct_path()
    {
        var ap = new Vec2(2, 2);
        var point = new Vec2(6, 2);
        Wall wall = Line("W1", 0, 0, 10, 0);
        var diffraction = new PathEngine(new SimulationScene([wall], Catalog), new AccessPoint(ap), Settings);

        double direct = PathLoss.FreeSpaceDbFromMetres(Settings.FrequencyMHz, 4);
        double reflected = PathLoss.FreeSpaceDbFromMetres(Settings.FrequencyMHz, Math.Sqrt(32)) + Fresnel.ReflectionLossDb(new Complex(4, 0), Math.Cos(Math.PI / 4));
        double expected = -10 * Math.Log10(Math.Pow(10, -direct / 10) + Math.Pow(10, -reflected / 10));

        Assert.Equal(direct, diffraction.CombinedLossDb(point), 6);
        Assert.Equal(expected, Engine([wall], ap).CombinedLossDb(point), 6);
    }

    [Fact]
    public void No_bounce_when_the_point_is_on_the_other_side()
    {
        PathEngine engine = Engine([Line("W1", 0, 0, 10, 0)], new Vec2(2, 2));

        Assert.Empty(Reflected(engine, new Vec2(6, -2)));
    }

    [Fact]
    public void No_bounce_when_the_reflection_point_falls_outside_the_segment()
    {
        PathEngine engine = Engine([Line("W1", 0, 0, 3, 0)], new Vec2(2, 2));

        Assert.Empty(Reflected(engine, new Vec2(6, 2)));
    }

    [Fact]
    public void A_column_reflects_only_on_its_outer_side()
    {
        Wall column = Wall.Column("C1", Polygon2.Rectangle(new BoundingBox(new Vec2(4, 4), new Vec2(5, 5))), "concrete", Reinforcement.Medium);
        PathEngine engine = Engine([column], new Vec2(2, 6));

        PathContribution path = Assert.Single(Reflected(engine, new Vec2(7, 6)));

        Assert.Equal(4.5, path.Vertices[0].X, 9);
        Assert.Equal(5, path.Vertices[0].Y, 9);
        Assert.True(path.Reflection!.IsColumn);
        Assert.All(engine.Surfaces, s => Assert.True(s.OutwardOnly));
    }

    [Fact]
    public void Another_wall_on_a_leg_adds_its_loss()
    {
        var ap = new Vec2(2, 2);
        var point = new Vec2(6, 2);
        PathContribution clear = Reflected(Engine([Line("W1", 0, 0, 10, 0)], ap), point).Single(p => p.Reflection!.WallId == "W1");
        PathContribution blocked = Reflected(Engine([Line("W1", 0, 0, 10, 0), Line("W2", 3, 0.2, 3, 1.5, "plain")], ap), point)
            .Single(p => p.Reflection!.WallId == "W1");

        Assert.Equal(clear.LossDb + 5, blocked.LossDb, 9);
    }

    [Fact]
    public void A_material_without_electrical_properties_does_not_reflect()
    {
        PathEngine engine = Engine([Line("W1", 0, 0, 10, 0, "plain")], new Vec2(2, 2));

        Assert.Empty(engine.Surfaces);
        Assert.Empty(Reflected(engine, new Vec2(6, 2)));
    }

    [Fact]
    public void The_diffraction_model_has_no_surfaces()
    {
        var engine = new PathEngine(new SimulationScene([Line("W1", 0, 0, 10, 0)], Catalog), new AccessPoint(new Vec2(2, 2)), Settings);

        Assert.Equal(PropagationModel.Diffraction, engine.Model);
        Assert.Empty(engine.Surfaces);
    }

    [Fact]
    public void The_parallel_grid_matches_the_point_by_point_result()
    {
        ReferenceScene reference = ReferenceData.Scene("mixta");
        var engine = new PathEngine(reference.Scene(), reference.AccessPoint, reference.Settings, PropagationModel.DiffractionAndReflection);
        Polygon2 area = Polygon2.Rectangle(new BoundingBox(new Vec2(-4, -2), new Vec2(7, 2)));
        GridSpec spec = GridSpec.Cover(area.Bounds, 0.5);

        RssiGrid grid = GridCalculator.Calculate(engine, spec, area);

        for (int column = 0; column < spec.Columns; column++)
        {
            for (int row = 0; row < spec.Rows; row++)
            {
                if (!grid.HasValue(column, row)) continue;
                Assert.Equal(engine.ReceivedPowerDbm(spec.CellCenter(column, row)), grid[column, row], 12);
            }
        }
    }

    [Fact]
    public void The_catalog_gives_every_material_its_electrical_properties()
    {
        Assert.All(ReferenceData.Catalog.All, m => Assert.NotNull(m.Reflection));
    }

    [Fact]
    public void The_query_describes_the_bounce()
    {
        var path = new PathContribution(PathKind.Reflected, [new Vec2(4, 0)], 5.66, 70, 0.3, new PathReflection("W1", false, 6.91, 45));
        var column = new PathContribution(PathKind.Reflected, [new Vec2(4.5, 5)], 5.39, 72, 0.2, new PathReflection("C1", true, 7.2, 22));

        Assert.Equal("Reflejado en muro W1 en (4; 0) · 45° · rebote 6,9 dB", Explanation.DescribePath(path));
        Assert.Equal("Reflejado en columna C1 en (4,5; 5) · 22° · rebote 7,2 dB", Explanation.DescribePath(column));
        Assert.Equal(0, path.Diffractions);
    }
}
