using WifiHeat.Core.Geometry;
using Xunit;

namespace WifiHeat.Core.Tests.Geometry;

public class PolygonTests
{
    private static readonly Polygon2 LShape = new(
        [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 4), new Vec2(4, 4), new Vec2(4, 8), new Vec2(0, 8)]);

    [Theory]
    [InlineData(2, 2, true)]
    [InlineData(8, 2, true)]
    [InlineData(2, 7, true)]
    [InlineData(7, 6, false)]
    [InlineData(-0.1, 2, false)]
    [InlineData(11, 2, false)]
    [InlineData(2, 8.1, false)]
    public void Contains_follows_a_concave_outline(double x, double y, bool expected)
    {
        Assert.Equal(expected, LShape.Contains(new Vec2(x, y)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(5, 0)]
    [InlineData(4, 6)]
    [InlineData(4, 4)]
    public void Boundary_and_vertices_count_as_inside(double x, double y)
    {
        Assert.True(LShape.Contains(new Vec2(x, y)));
    }

    [Fact]
    public void Rectangle_matches_its_box()
    {
        var box = new BoundingBox(new Vec2(1, 2), new Vec2(31, 16));
        Polygon2 rectangle = Polygon2.Rectangle(box);

        Assert.Equal(box, rectangle.Bounds);
        Assert.Equal(420, rectangle.AreaM2, 9);
        Assert.True(rectangle.Contains(new Vec2(30.99, 15.99)));
        Assert.False(rectangle.Contains(new Vec2(31.01, 10)));
    }

    [Fact]
    public void Repeated_closing_vertex_is_dropped()
    {
        var square = new Polygon2([new Vec2(0, 0), new Vec2(2, 0), new Vec2(2, 2), new Vec2(0, 2), new Vec2(0, 0)]);

        Assert.Equal(4, square.Vertices.Count);
        Assert.Equal(4, square.AreaM2, 9);
    }

    [Fact]
    public void Degenerate_outlines_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => new Polygon2([new Vec2(0, 0), new Vec2(1, 0)]));
        Assert.Throws<ArgumentException>(() => new Polygon2([new Vec2(0, 0), new Vec2(1, 0), new Vec2(2, 0)]));
    }

    [Theory]
    [InlineData(7, 6, false)]
    [InlineData(3.8, 5, true)]
    [InlineData(9.8, 3.8, true)]
    [InlineData(12, 2, false)]
    [InlineData(4, 6, false)]
    [InlineData(10, 1, false)]
    public void Overlaps_detects_cells_that_touch_the_area(double minX, double minY, bool expected)
    {
        var cell = new BoundingBox(new Vec2(minX, minY), new Vec2(minX + 0.5, minY + 0.5));

        Assert.Equal(expected, LShape.Overlaps(cell));
    }

    [Fact]
    public void Overlaps_a_cell_that_encloses_a_small_polygon()
    {
        var tiny = new Polygon2([new Vec2(1.1, 1.1), new Vec2(1.3, 1.1), new Vec2(1.2, 1.3)]);

        Assert.True(tiny.Overlaps(new BoundingBox(new Vec2(1, 1), new Vec2(1.5, 1.5))));
        Assert.False(tiny.Overlaps(new BoundingBox(new Vec2(1.5, 1), new Vec2(2, 1.5))));
    }
}

public class PolygonInteriorTests
{
    private static readonly Polygon2 Square = new([new Vec2(0, 0), new Vec2(1, 0), new Vec2(1, 1), new Vec2(0, 1)]);

    private static readonly Polygon2 LShape = new(
        [new Vec2(0, 0), new Vec2(10, 0), new Vec2(10, 4), new Vec2(4, 4), new Vec2(4, 8), new Vec2(0, 8)]);

    private static Segment2 S(double ax, double ay, double bx, double by) => new(new Vec2(ax, ay), new Vec2(bx, by));

    [Theory]
    [InlineData(0.5, 0.5, true)]
    [InlineData(0.00001, 0.5, true)]
    [InlineData(0, 0.5, false)]
    [InlineData(0.0000005, 0.5, false)]
    [InlineData(1, 1, false)]
    [InlineData(-0.5, 0.5, false)]
    [InlineData(1.5, 0.5, false)]
    public void ContainsStrictly_leaves_out_the_boundary(double x, double y, bool expected)
    {
        Assert.Equal(expected, Square.ContainsStrictly(new Vec2(x, y)));
    }

    [Fact]
    public void A_segment_across_the_square_has_one_interior_stretch()
    {
        (double from, double to) = Assert.Single(Square.InteriorIntervals(S(-1, 0.5, 3, 0.5)));

        Assert.Equal(0.25, from, 12);
        Assert.Equal(0.5, to, 12);
    }

    [Theory]
    [InlineData(-1, 0, 2, 0)]
    [InlineData(0.2, 1, 0.8, 1)]
    [InlineData(-1, 1, 1, -1)]
    [InlineData(-1, 2, 3, 2)]
    [InlineData(1, 0.5, 2, 0.5)]
    public void Sides_corners_and_misses_are_not_interior(double ax, double ay, double bx, double by)
    {
        Assert.Empty(Square.InteriorIntervals(S(ax, ay, bx, by)));
    }

    [Fact]
    public void A_corner_to_corner_diagonal_is_interior()
    {
        (double from, double to) = Assert.Single(Square.InteriorIntervals(S(0, 0, 1, 1)));

        Assert.Equal(0, from, 12);
        Assert.Equal(1, to, 12);
    }

    [Fact]
    public void A_segment_fully_inside_is_one_whole_stretch()
    {
        Assert.Equal((0.0, 1.0), Assert.Single(Square.InteriorIntervals(S(0.2, 0.2, 0.8, 0.7))));
    }

    [Fact]
    public void A_concave_outline_can_give_two_stretches()
    {
        IReadOnlyList<(double From, double To)> intervals = LShape.InteriorIntervals(S(2, 9, 11, 0));

        Assert.Equal(2, intervals.Count);
        Assert.Equal(1.0 / 9, intervals[0].From, 9);
        Assert.Equal(2.0 / 9, intervals[0].To, 9);
        Assert.Equal(5.0 / 9, intervals[1].From, 9);
        Assert.Equal(8.0 / 9, intervals[1].To, 9);
    }

    [Fact]
    public void A_point_segment_has_no_interior()
    {
        Assert.Empty(Square.InteriorIntervals(S(0.5, 0.5, 0.5, 0.5)));
    }
}
