using WifiHeat.Core.Geometry;
using WifiHeat.Core.Walls;
using Xunit;

namespace WifiHeat.Core.Tests.Walls;

public class WallAxisDetectorTests
{
    private static FaceSegment F(string id, double ax, double ay, double bx, double by) =>
        new(id, new Segment2(new Vec2(ax, ay), new Vec2(bx, by)));

    private static void Near(Vec2 expected, Vec2 actual) => Assert.True(Vec2.Distance(expected, actual) < 1e-6, $"{expected} != {actual}");

    private static bool HasEnds(WallAxis axis, Vec2 a, Vec2 b) =>
        (Vec2.Distance(axis.Axis.A, a) < 1e-6 && Vec2.Distance(axis.Axis.B, b) < 1e-6) ||
        (Vec2.Distance(axis.Axis.A, b) < 1e-6 && Vec2.Distance(axis.Axis.B, a) < 1e-6);

    private static List<FaceSegment> Room() =>
    [
        F("ob", 0, 0, 10, 0), F("or", 10, 0, 10, 8), F("ot", 10, 8, 0, 8), F("ol", 0, 8, 0, 0),
        F("ib", 0.2, 0.2, 9.8, 0.2), F("ir", 9.8, 0.2, 9.8, 7.8), F("it", 9.8, 7.8, 0.2, 7.8), F("il", 0.2, 7.8, 0.2, 0.2),
    ];

    [Fact]
    public void Two_parallel_faces_give_one_centred_axis_with_their_distance_as_thickness()
    {
        AxisDetection result = WallAxisDetector.Detect([F("a", 0, 0, 5, 0), F("b", 5, 0.2, 0, 0.2)]);

        WallAxis axis = Assert.Single(result.Axes);
        Assert.True(HasEnds(axis, new Vec2(0, 0.1), new Vec2(5, 0.1)));
        Assert.Equal(0.2, axis.ThicknessM, 9);
        Assert.Equal(["a", "b"], axis.FaceIds.Order());
        Assert.Empty(result.Unpaired);
    }

    [Fact]
    public void Closed_room_gives_four_axes_meeting_at_the_corners()
    {
        AxisDetection result = WallAxisDetector.Detect(Room());

        Assert.Equal(4, result.Axes.Count);
        Assert.Contains(result.Axes, a => HasEnds(a, new Vec2(0.1, 0.1), new Vec2(9.9, 0.1)));
        Assert.Contains(result.Axes, a => HasEnds(a, new Vec2(9.9, 0.1), new Vec2(9.9, 7.9)));
        Assert.Contains(result.Axes, a => HasEnds(a, new Vec2(9.9, 7.9), new Vec2(0.1, 7.9)));
        Assert.Contains(result.Axes, a => HasEnds(a, new Vec2(0.1, 7.9), new Vec2(0.1, 0.1)));
        Assert.Empty(result.Unpaired);
    }

    [Fact]
    public void T_junction_keeps_the_through_wall_continuous_and_extends_the_stem_to_its_axis()
    {
        List<FaceSegment> faces = Room();
        faces.RemoveAll(f => f.Id is "ib" or "it");
        faces.AddRange(
        [
            F("ib1", 0.2, 0.2, 4.95, 0.2), F("ib2", 5.05, 0.2, 9.8, 0.2),
            F("it1", 0.2, 7.8, 4.95, 7.8), F("it2", 5.05, 7.8, 9.8, 7.8),
            F("sl", 4.95, 0.2, 4.95, 7.8), F("sr", 5.05, 0.2, 5.05, 7.8),
        ]);

        AxisDetection result = WallAxisDetector.Detect(faces);

        Assert.Equal(5, result.Axes.Count);
        Assert.Contains(result.Axes, a => HasEnds(a, new Vec2(0.1, 0.1), new Vec2(9.9, 0.1)));
        WallAxis stem = Assert.Single(result.Axes, a => Math.Abs(a.ThicknessM - 0.1) < 1e-9);
        Assert.True(HasEnds(stem, new Vec2(5, 0.1), new Vec2(5, 7.9)));
    }

    [Fact]
    public void Door_opening_on_both_faces_stays_open()
    {
        AxisDetection result = WallAxisDetector.Detect(
        [
            F("a1", 0, 0, 2, 0), F("a2", 2.9, 0, 6, 0),
            F("b1", 0, 0.15, 2, 0.15), F("b2", 2.9, 0.15, 6, 0.15),
        ]);

        Assert.Equal(2, result.Axes.Count);
        Assert.Contains(result.Axes, a => HasEnds(a, new Vec2(0, 0.075), new Vec2(2, 0.075)));
        Assert.Contains(result.Axes, a => HasEnds(a, new Vec2(2.9, 0.075), new Vec2(6, 0.075)));
    }

    [Fact]
    public void Long_face_pairs_with_several_short_opposite_faces()
    {
        AxisDetection result = WallAxisDetector.Detect([F("long", 0, 0, 10, 0), F("s1", 0, 0.3, 3, 0.3), F("s2", 6, 0.3, 10, 0.3)]);

        Assert.Equal(2, result.Axes.Count);
        Assert.All(result.Axes, a => Assert.Equal(0.3, a.ThicknessM, 9));
        Assert.Equal(["long"], result.PartlyUnpaired);
    }

    [Theory]
    [InlineData(0.6)]
    [InlineData(0.01)]
    public void Faces_too_far_apart_or_too_close_are_not_paired(double distance)
    {
        AxisDetection result = WallAxisDetector.Detect([F("a", 0, 0, 5, 0), F("b", 0, distance, 5, distance)]);

        Assert.Empty(result.Axes);
        Assert.Equal(["a", "b"], result.Unpaired.Order());
    }

    [Fact]
    public void Faces_that_barely_overlap_are_not_paired()
    {
        AxisDetection result = WallAxisDetector.Detect([F("a", 0, 0, 5, 0), F("b", 4.95, 0.2, 9, 0.2)]);

        Assert.Empty(result.Axes);
    }

    [Fact]
    public void Non_parallel_lines_are_not_paired()
    {
        AxisDetection result = WallAxisDetector.Detect([F("a", 0, 0, 5, 0), F("b", 0, 0.2, 5, 0.4)]);

        Assert.Empty(result.Axes);
    }

    [Fact]
    public void A_face_is_paired_with_its_nearest_neighbour_only()
    {
        AxisDetection result = WallAxisDetector.Detect([F("a", 0, 0, 5, 0), F("b", 0, 0.15, 5, 0.15), F("c", 0, 0.45, 5, 0.45)]);

        WallAxis axis = Assert.Single(result.Axes);
        Assert.Equal(0.15, axis.ThicknessM, 9);
        Assert.Equal(["c"], result.Unpaired);
    }

    [Fact]
    public void Diagonal_wall_is_detected()
    {
        double d = 0.2 / Math.Sqrt(2);
        AxisDetection result = WallAxisDetector.Detect([F("a", 0, 0, 4, 4), F("b", -d, d, 4 - d, 4 + d)]);

        WallAxis axis = Assert.Single(result.Axes);
        Assert.Equal(0.2, axis.ThicknessM, 6);
        Near(new Vec2(-d / 2, d / 2), axis.Axis.A);
    }

    [Fact]
    public void Window_keeps_only_segments_entirely_inside()
    {
        var window = new BoundingBox(new Vec2(0, 0), new Vec2(10, 10));

        IReadOnlyList<FaceSegment> inside = WallAxisDetector.InsideWindow(
            [F("in", 1, 1, 9, 1), F("cross", 5, 5, 15, 5), F("out", 20, 20, 30, 20)], window);

        Assert.Equal(["in"], inside.Select(f => f.Id));
    }

    [Fact]
    public void No_faces_gives_an_empty_result()
    {
        AxisDetection result = WallAxisDetector.Detect([]);

        Assert.Empty(result.Axes);
        Assert.Empty(result.Unpaired);
    }
}
