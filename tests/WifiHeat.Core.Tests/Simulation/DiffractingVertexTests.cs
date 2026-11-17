using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Tests.Data;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class DiffractingVertexTests
{
    public static TheoryData<string> SceneNames => [.. ReferenceData.Scenes.Select(s => s.Name)];

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

    private static IReadOnlyList<DiffractingVertex> Find(params Wall[] walls) =>
        DiffractingVertices.Find(new SimulationScene(walls, ReferenceData.Catalog), Band.Ghz5);

    [Fact]
    public void The_reference_has_thirteen_scenes_and_seventy_six_points()
    {
        Assert.Equal(13, ReferenceData.Scenes.Count);
        Assert.Equal(76, ReferenceData.Scenes.Sum(s => s.Points.Count));
    }

    [Theory]
    [MemberData(nameof(SceneNames))]
    public void Vertices_match_the_reference(string name)
    {
        ReferenceScene reference = ReferenceData.Scene(name);

        IReadOnlyList<DiffractingVertex> found = DiffractingVertices.Find(reference.Scene(), reference.Band);

        Assert.Equal(reference.Vertices.Count, found.Count);
        foreach (ReferenceVertex expected in reference.Vertices)
        {
            DiffractingVertex vertex = Assert.Single(found, v => Vec2.Distance(v.Position, expected.Position) <= 1e-6);
            Assert.Equal(expected.FreeSectorDeg, vertex.FreeSectorDeg, 0.01);
            Assert.Equal(expected.LossDb, vertex.LossDb, 0.001);
        }
    }

    [Fact]
    public void A_free_wall_end_has_the_whole_turn_free()
    {
        IReadOnlyList<DiffractingVertex> found = Find(WallOf("W", "concrete", (0, 0), (0, 5)));

        Assert.Equal(2, found.Count);
        Assert.All(found, v => Assert.Equal(360, v.FreeSectorDeg, 6));
        Assert.All(found, v => Assert.Equal(44.8, v.LossDb, 9));
        Assert.All(found, v => Assert.Equal(Math.Pow(10, -4.48), v.Transmission, 12));
    }

    [Fact]
    public void An_L_corner_leaves_three_quarters_free_and_takes_the_larger_loss()
    {
        IReadOnlyList<DiffractingVertex> found = Find(
            WallOf("A", "solid-brick", (0, 0), (0, -10)),
            WallOf("B", "drywall", (0, 0), (10, 0)));

        DiffractingVertex corner = Assert.Single(found, v => v.Position == new Vec2(0, 0));
        Assert.Equal(270, corner.FreeSectorDeg, 6);
        Assert.Equal(17.5, corner.LossDb, 9);
        Assert.Equal(3, found.Count);
    }

    [Fact]
    public void A_corner_inside_a_polyline_is_a_vertex_too()
    {
        IReadOnlyList<DiffractingVertex> found = Find(WallOf("P", "concrete", (0, 5), (0, 0), (5, 0)));

        Assert.Equal(270, Assert.Single(found, v => v.Position == new Vec2(0, 0)).FreeSectorDeg, 6);
        Assert.Equal(3, found.Count);
    }

    [Fact]
    public void A_T_junction_is_not_a_vertex()
    {
        IReadOnlyList<DiffractingVertex> found = Find(
            WallOf("A", "concrete", (0, -5), (0, 5)),
            WallOf("B", "drywall", (0, 0), (5, 0)));

        Assert.Equal(3, found.Count);
        Assert.DoesNotContain(found, v => v.Position == new Vec2(0, 0));
    }

    [Fact]
    public void A_crossing_is_not_a_vertex()
    {
        IReadOnlyList<DiffractingVertex> found = Find(
            WallOf("A", "concrete", (0, -5), (0, 0), (0, 5)),
            WallOf("B", "drywall", (-5, 0), (0, 0), (5, 0)));

        Assert.Equal(4, found.Count);
        Assert.DoesNotContain(found, v => v.Position == new Vec2(0, 0));
    }

    [Fact]
    public void A_straight_continuation_is_not_a_vertex()
    {
        IReadOnlyList<DiffractingVertex> found = Find(
            WallOf("A", "concrete", (0, 0), (5, 0)),
            WallOf("B", "concrete", (5.0005, 0), (9, 0)));

        Assert.Equal(2, found.Count);
        Assert.DoesNotContain(found, v => Math.Abs(v.Position.X - 5) < 0.01);
    }

    [Fact]
    public void A_shallow_bend_is_not_a_vertex()
    {
        double halfDegree = Math.PI / 360;
        IReadOnlyList<DiffractingVertex> found = Find(
            WallOf("A", "concrete", (-5, 0), (0, 0), (5 * Math.Cos(halfDegree), 5 * Math.Sin(halfDegree))));

        Assert.Equal(2, found.Count);
        Assert.DoesNotContain(found, v => v.Position == new Vec2(0, 0));
    }

    [Fact]
    public void Column_corners_are_vertices_with_the_column_loss()
    {
        IReadOnlyList<DiffractingVertex> found = Find(Column("C", 0, 0));

        Assert.Equal(4, found.Count);
        Assert.All(found, v => Assert.Equal(270, v.FreeSectorDeg, 6));
        Assert.All(found, v => Assert.Equal(49.8, v.LossDb, 9));
    }

    [Fact]
    public void A_wall_end_inside_a_column_is_discarded()
    {
        IReadOnlyList<DiffractingVertex> found = Find(Column("C", 0, 0), WallOf("W", "drywall", (0.15, 0.15), (5, 0.15)));

        Assert.DoesNotContain(found, v => v.Position == new Vec2(0.15, 0.15));
        Assert.Contains(found, v => v.Position == new Vec2(5, 0.15));
        Assert.Equal(5, found.Count);
    }

    [Fact]
    public void A_wall_ending_on_a_column_face_is_not_a_vertex()
    {
        IReadOnlyList<DiffractingVertex> found = Find(Column("C", 0, 0), WallOf("W", "drywall", (0.3, 0.15), (5, 0.15)));

        Assert.DoesNotContain(found, v => v.Position == new Vec2(0.3, 0.15));
        Assert.Equal(5, found.Count);
    }
}
