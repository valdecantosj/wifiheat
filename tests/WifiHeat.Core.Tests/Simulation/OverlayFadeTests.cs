using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class OverlayFadeTests
{
    [Theory]
    [InlineData(0, 255)]
    [InlineData(40, 153)]
    [InlineData(90, 26)]
    public void Fade_maps_to_the_equivalent_opacity(int percent, byte alpha)
    {
        Assert.Equal(alpha, OverlayFade.Alpha(percent));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(91)]
    public void Out_of_range_fade_is_rejected_and_the_previous_value_kept(int percent)
    {
        var session = new SimulationSession(new MaterialCatalog([]));

        Assert.False(session.SetOverlayFade(percent).Succeeded);
        Assert.Equal(OverlayFade.DefaultPercent, session.OverlayFadePercent);
    }

    [Fact]
    public void Accepted_fade_is_kept_in_the_session()
    {
        var session = new SimulationSession(new MaterialCatalog([]));

        Assert.True(session.SetOverlayFade(0).Succeeded);
        Assert.Equal(0, session.OverlayFadePercent);
    }

    [Fact]
    public void Default_image_is_opaque_inside_the_area_and_transparent_outside()
    {
        var values = new double[4, 4];
        for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) values[c, r] = -60 - c - r;
        var spec = new GridSpec(new Vec2(0, 0), 0.5, 4, 4);
        var triangle = new Polygon2([new Vec2(0, 0), new Vec2(2, 0), new Vec2(0, 2)]);

        HeatmapImage image = HeatmapRasterizer.Render(new RssiGrid(spec, values, triangle));

        var alphas = Enumerable.Range(0, image.Width * image.Height).Select(i => image.Bgra[i * 4 + 3]).Distinct().Order().ToList();
        Assert.Equal([0, 255], alphas);
    }
}
