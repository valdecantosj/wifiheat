using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Tests.Data;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class LegLossTests
{
    private const double ColumnLossDb = 49.8;
    private const double ConcreteLossDb = 44.8;

    private static Wall WallOf(string id, string material, params (double X, double Y)[] points)
    {
        var segments = new List<Segment2>();
        for (int i = 0; i + 1 < points.Length; i++)
        {
            segments.Add(new Segment2(new Vec2(points[i].X, points[i].Y), new Vec2(points[i + 1].X, points[i + 1].Y)));
        }

        return new Wall(id, segments, material, 0.15);
    }

    private static Wall Column(string id, double x, double y, double side = 0.3) =>
        Wall.Column(id, Polygon2.Rectangle(new BoundingBox(new Vec2(x, y), new Vec2(x + side, y + side))), "concrete", Reinforcement.Medium);

    private static SimulationScene SceneOf(params Wall[] walls) => new(walls, ReferenceData.Catalog);

    private static double Leg(SimulationScene scene, (double X, double Y) a, (double X, double Y) b, bool trimA = false, bool trimB = false)
    {
        Vec2[] vertices = [.. DiffractingVertices.Find(scene, Band.Ghz5).Select(v => v.Position)];
        return LegLoss.LossDb(scene, Band.Ghz5, new Vec2(a.X, a.Y), new Vec2(b.X, b.Y), trimA, trimB, vertices);
    }

    private static double StraightLine(SimulationScene scene, (double X, double Y) a, (double X, double Y) b) =>
        PathCrossings.TotalLossDb(scene, new Segment2(new Vec2(a.X, a.Y), new Vec2(b.X, b.Y)), Band.Ghz5);

    [Fact]
    public void A_leg_through_a_column_counts_it_once()
    {
        SimulationScene scene = SceneOf(Column("C", 0, 0));

        Assert.Equal(ColumnLossDb, Leg(scene, (-3, 0.15), (3.3, 0.15)), 9);
        Assert.Equal(ColumnLossDb, Leg(scene, (-3, 0.1), (3, 0.25)), 9);
    }

    [Theory]
    [InlineData(-1, 0, 1, 0)]
    [InlineData(-1, 0.3, 1, 0.3)]
    [InlineData(0, -1, 0, 1)]
    [InlineData(0.1, 0, 0.2, 0)]
    public void A_leg_running_along_a_column_face_does_not_count_it(double ax, double ay, double bx, double by)
    {
        SimulationScene scene = SceneOf(Column("C", 0, 0));

        Assert.Equal(0, Leg(scene, (ax, ay), (bx, by)));
        Assert.Equal(ColumnLossDb, StraightLine(scene, (ax, ay), (bx, by)), 9);
    }

    [Fact]
    public void A_diagonal_through_the_interior_counts_the_column()
    {
        SimulationScene scene = SceneOf(Column("C", 0, 0));

        Assert.Equal(ColumnLossDb, Leg(scene, (-1, -1), (1.3, 1.3)), 9);
        Assert.Equal(ColumnLossDb, Leg(scene, (0, 0), (0.3, 0.3)), 9);
        Assert.Equal(ColumnLossDb, Leg(scene, (0, 0), (0.3, 0.3), trimA: true, trimB: true), 9);
    }

    [Fact]
    public void Touching_a_column_corner_does_not_count_it()
    {
        SimulationScene scene = SceneOf(Column("C", 0, 0));

        Assert.Equal(0, Leg(scene, (-1, 1), (1, -1)));
        Assert.Equal(ColumnLossDb, StraightLine(scene, (-1, 1), (1, -1)), 9);
    }

    [Fact]
    public void A_grazing_touch_on_a_free_wall_end_does_not_count_the_wall()
    {
        SimulationScene scene = SceneOf(WallOf("W", "concrete", (0, 0), (0, 50)));

        Assert.Equal(0, Leg(scene, (-3, -3), (3, 3)));
        Assert.Equal(ConcreteLossDb, StraightLine(scene, (-3, -3), (3, 3)), 9);
    }

    [Fact]
    public void A_crossing_next_to_the_free_end_still_counts()
    {
        SimulationScene scene = SceneOf(WallOf("W", "concrete", (0, 0), (0, 50)));

        Assert.Equal(ConcreteLossDb, Leg(scene, (-3, 0.00001), (3, 0.00001)), 9);
        Assert.Equal(0, Leg(scene, (-3, -0.00001), (3, -0.00001)));
    }

    [Fact]
    public void A_touch_on_an_end_that_is_not_a_vertex_counts_with_the_junction_rule()
    {
        SimulationScene scene = SceneOf(
            WallOf("A", "solid-brick", (0, -5), (0, 5)),
            WallOf("B", "drywall", (0, 0), (5, 0)));

        Assert.Equal(17.5, Leg(scene, (-3, -3), (3, 3)), 9);
    }

    [Fact]
    public void A_leg_running_along_a_wall_does_not_count_it()
    {
        SimulationScene scene = SceneOf(WallOf("W", "concrete", (0, 0), (5, 0)));

        Assert.Equal(0, Leg(scene, (-1, 0), (6, 0)));
        Assert.Equal(0, Leg(scene, (1, 0), (4, 0)));
        Assert.Equal(ConcreteLossDb, StraightLine(scene, (-1, 0), (6, 0)), 9);
    }

    [Fact]
    public void The_trim_leaves_out_what_touches_the_vertex_end()
    {
        SimulationScene scene = SceneOf(WallOf("W", "concrete", (0, -1), (0, 1)));

        Assert.Equal(ConcreteLossDb, Leg(scene, (-3, 0), (0.001, 0)), 9);
        Assert.Equal(0, Leg(scene, (-3, 0), (0.001, 0), trimB: true));
        Assert.Equal(ConcreteLossDb, Leg(scene, (-0.001, 0), (3, 0)), 9);
        Assert.Equal(0, Leg(scene, (-0.001, 0), (3, 0), trimA: true));
        Assert.Equal(ConcreteLossDb, Leg(scene, (-3, 0), (0.003, 0), trimB: true), 9);
    }

    [Fact]
    public void A_leg_shorter_than_its_trims_has_no_loss()
    {
        SimulationScene scene = SceneOf(WallOf("W", "concrete", (0, -1), (0, 1)));

        Assert.Equal(0, Leg(scene, (-0.002, 0), (0.002, 0), trimA: true, trimB: true));
        Assert.Equal(0, Leg(scene, (1, 1), (1, 1)));
    }

    [Fact]
    public void A_leg_from_a_corner_does_not_count_the_walls_of_the_corner()
    {
        SimulationScene scene = SceneOf(
            WallOf("A", "concrete", (0, 0), (0, -10)),
            WallOf("B", "concrete", (0, 0), (10, 0)));

        Assert.Equal(0, Leg(scene, (-2, -5), (0, 0), trimB: true));
        Assert.Equal(0, Leg(scene, (0, 0), (-1, 3), trimA: true));
        Assert.Equal(0, Leg(scene, (0, 0), (3, -2), trimA: true));
        Assert.Equal(ConcreteLossDb, Leg(scene, (3, -2), (5, 2)), 9);
    }

    [Fact]
    public void Walls_crossed_apart_add_up_and_a_junction_counts_once()
    {
        SimulationScene scene = SceneOf(
            WallOf("A", "solid-brick", (2, -5), (2, 5)),
            WallOf("B", "drywall", (4, -5), (4, 5)),
            WallOf("C", "glass", (4, 0), (9, 0)));

        Assert.Equal(17.5 + 10.1, Leg(scene, (0, 1), (6, 1)), 9);
        Assert.Equal(17.5 + 10.1, Leg(scene, (0, -1), (8, 1)), 9);
        Assert.Equal(17.5 + 10.1 + 3.5, Leg(scene, (0, 3), (8, -1)), 9);
    }
}
