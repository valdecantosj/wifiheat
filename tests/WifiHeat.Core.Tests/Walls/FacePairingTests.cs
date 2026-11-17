using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Walls;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Walls;

public class FacePairingTests
{
    private static readonly MaterialCatalog Catalog = new([new Material("hollow", "Ladrillo hueco", 6, 10)]);

    private static Segment2 S(double ax, double ay, double bx, double by) => new(new Vec2(ax, ay), new Vec2(bx, by));

    private static FaceEntity Line(string id, double ax, double ay, double bx, double by) => new(id, [S(ax, ay, bx, by)]);

    private static Wall WallOf(string id, params Segment2[] segments) => new(id, segments, "hollow", 0.15);

    [Fact]
    public void Two_faces_of_a_wall_give_one_axis_named_by_entity()
    {
        FacePairs pairs = FacePairing.Detect([Line("A", 0, 0, 5, 0), Line("B", 0, 0.2, 5, 0.2), Line("C", 0, 3, 5, 3)]);

        WallAxis axis = Assert.Single(pairs.Axes);
        Assert.Equal(0.2, axis.ThicknessM, 9);
        Assert.Equal(0.1, axis.Axis.A.Y, 9);
        Assert.Equal(["A", "B"], axis.FaceIds.Order());
        Assert.Equal(["A", "B"], pairs.Paired);
        Assert.Equal(["C"], pairs.Single);
        Assert.Empty(pairs.WithLeftover);
    }

    [Fact]
    public void A_polyline_is_one_entity_with_its_paired_and_unpaired_stretches()
    {
        var room = new FaceEntity("P", [S(0, 0, 6, 0), S(6, 0, 6, 4), S(6, 4, 0, 4), S(0, 4, 0, 0)]);

        FacePairs pairs = FacePairing.Detect([room, Line("I", 0.2, 0.2, 5.8, 0.2)]);

        Assert.Equal(["P"], Assert.Single(pairs.Axes).FaceIds.Where(id => id != "I"));
        Assert.Equal(["P", "I"], pairs.Paired);
        Assert.Equal(["P"], pairs.WithLeftover);
    }

    [Fact]
    public void A_long_face_with_a_short_opposite_face_has_leftover()
    {
        FacePairs pairs = FacePairing.Detect([Line("L", 0, 0, 10, 0), Line("S", 0, 0.2, 3, 0.2)]);

        Assert.Single(pairs.Axes);
        Assert.Equal(["L"], pairs.WithLeftover);
    }

    [Fact]
    public void Segments_of_the_same_entity_do_not_pair_when_disabled()
    {
        var thin = new FaceEntity("P", [S(0, 0, 5, 0), S(5, 0, 5, 0.2), S(5, 0.2, 0, 0.2), S(0, 0.2, 0, 0)]);

        Assert.Single(FacePairing.Detect([thin]).Axes);
        Assert.Empty(FacePairing.Detect([thin], new AxisDetectionOptions { PairWithinSameEntity = false }).Axes);
    }

    [Fact]
    public void Parallel_walls_closer_than_half_a_metre_form_a_group()
    {
        IReadOnlyList<ParallelWallGroup> groups = ParallelWalls.Find(
        [
            WallOf("1503B", S(5, 0, 5, 8)),
            WallOf("15042", S(5.2, 0, 5.2, 8)),
            WallOf("FAR", S(9, 0, 9, 8)),
        ]);

        ParallelWallGroup group = Assert.Single(groups);
        Assert.Equal(["1503B", "15042"], group.WallIds.Order(StringComparer.Ordinal));
        Assert.Equal(0.2, group.SeparationM, 9);
        Assert.True(group.Convertible);
    }

    [Theory]
    [InlineData(0.6)]
    [InlineData(0.02)]
    public void Walls_outside_the_face_separation_range_are_not_a_group(double separation)
    {
        Assert.Empty(ParallelWalls.Find([WallOf("A", S(5, 0, 5, 8)), WallOf("B", S(5 + separation, 0, 5 + separation, 8))]));
    }

    [Fact]
    public void A_closed_polyline_wall_is_not_paired_with_itself()
    {
        Assert.Empty(ParallelWalls.Find([WallOf("P", S(0, 0, 5, 0), S(5, 0, 5, 0.3), S(5, 0.3, 0, 0.3), S(0, 0.3, 0, 0)), WallOf("FAR", S(0, 5, 5, 5))]));
    }

    [Fact]
    public void A_group_with_a_long_unpaired_stretch_is_not_convertible()
    {
        ParallelWallGroup group = Assert.Single(ParallelWalls.Find([WallOf("L", S(0, 0, 10, 0)), WallOf("S", S(0, 0.2, 3, 0.2))]));

        Assert.False(group.Convertible);
    }

    [Fact]
    public void Validation_warns_about_double_counting_with_both_handles()
    {
        ValidationReport report = SceneValidator.Validate([WallOf("1503B", S(5, 0, 5, 8)), WallOf("15042", S(5.2, 0, 5.2, 8))], Catalog);

        ValidationIssue issue = Assert.Single(report.Issues, i => i.Code == ValidationIssue.DoubleCount);
        Assert.Equal(IssueSeverity.Warning, issue.Severity);
        Assert.Contains("1503B", issue.Message);
        Assert.Contains("15042", issue.Message);
        Assert.Contains("0,2 m", issue.Message);
        Assert.True(report.IsValid);
    }

    [Fact]
    public void Validation_has_no_double_count_warning_for_a_normal_room()
    {
        ValidationReport report = SceneValidator.Validate(
        [
            WallOf("A", S(0, 0, 10, 0)), WallOf("B", S(10, 0, 10, 8)), WallOf("C", S(10, 8, 0, 8)), WallOf("D", S(0, 8, 0, 0)), WallOf("E", S(5, 0, 5, 8)),
        ], Catalog);

        Assert.DoesNotContain(report.Issues, i => i.Code == ValidationIssue.DoubleCount);
    }
}
