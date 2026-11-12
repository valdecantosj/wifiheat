using WifiHeat.Core.Geometry;
using Xunit;

namespace WifiHeat.Core.Tests.Geometry;

public class SegmentIntersectionTests
{
    private static Segment2 S(double ax, double ay, double bx, double by) => new(new Vec2(ax, ay), new Vec2(bx, by));

    [Fact]
    public void Proper_crossing_is_detected() => Assert.True(SegmentIntersection.Intersects(S(0, 0, 10, 0), S(5, -1, 5, 1)));

    [Fact]
    public void Separate_segments_do_not_intersect() => Assert.False(SegmentIntersection.Intersects(S(0, 0, 4, 0), S(5, -1, 5, 1)));

    [Fact]
    public void Parallel_non_collinear_do_not_intersect() => Assert.False(SegmentIntersection.Intersects(S(0, 0, 10, 0), S(0, 1, 10, 1)));

    [Fact]
    public void Collinear_overlap_counts_as_intersection() => Assert.True(SegmentIntersection.Intersects(S(0, 0, 10, 0), S(5, 0, 15, 0)));

    [Fact]
    public void Collinear_disjoint_do_not_intersect() => Assert.False(SegmentIntersection.Intersects(S(0, 0, 4, 0), S(5, 0, 15, 0)));

    [Fact]
    public void Touching_wall_end_counts() => Assert.True(SegmentIntersection.Intersects(S(0, 0, 10, 0), S(5, 0, 5, 3)));

    [Fact]
    public void Path_ending_on_wall_counts() => Assert.True(SegmentIntersection.Intersects(S(0, 0, 5, 0), S(5, -1, 5, 1)));

    [Fact]
    public void Path_stopping_before_wall_does_not_count() => Assert.False(SegmentIntersection.Intersects(S(0, 0, 4.999, 0), S(5, -1, 5, 1)));
}
