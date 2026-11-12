using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class HeatmapRasterizerTests
{
    private static RssiGrid Grid(double[,] values, Polygon2? area = null) =>
        new(new GridSpec(new Vec2(0, 0), 1, values.GetLength(0), values.GetLength(1)), values, area);

    private static RssiGrid TwoByTwo() => Grid(new double[,] { { -50, -60 }, { -70, -80 } });

    [Theory]
    [InlineData(0.5, 0.5, -50)]
    [InlineData(0.5, 1.5, -60)]
    [InlineData(1.5, 0.5, -70)]
    [InlineData(1.5, 1.5, -80)]
    public void Cell_centres_return_the_cell_value(double x, double y, double expected)
    {
        Assert.Equal(expected, HeatmapRasterizer.Sample(TwoByTwo(), new Vec2(x, y)), 9);
    }

    [Theory]
    [InlineData(1.0, 0.5, -60)]
    [InlineData(0.5, 1.0, -55)]
    [InlineData(1.0, 1.0, -65)]
    [InlineData(1.25, 0.5, -65)]
    public void Between_centres_the_value_is_bilinear(double x, double y, double expected)
    {
        Assert.Equal(expected, HeatmapRasterizer.Sample(TwoByTwo(), new Vec2(x, y)), 9);
    }

    [Fact]
    public void Outside_the_grid_the_edge_value_is_kept()
    {
        Assert.Equal(-50, HeatmapRasterizer.Sample(TwoByTwo(), new Vec2(-3, 0.1)), 9);
        Assert.Equal(-80, HeatmapRasterizer.Sample(TwoByTwo(), new Vec2(5, 5)), 9);
    }

    [Fact]
    public void Cells_without_value_are_ignored()
    {
        RssiGrid grid = Grid(new double[,] { { -50, double.NaN }, { -70, double.NaN } });

        Assert.Equal(-60, HeatmapRasterizer.Sample(grid, new Vec2(1.0, 1.0)), 9);
        Assert.Equal(-50, HeatmapRasterizer.Sample(grid, new Vec2(0.5, 1.2)), 9);
    }

    [Fact]
    public void All_neighbours_without_value_give_no_value()
    {
        RssiGrid grid = Grid(new double[,] { { double.NaN, double.NaN }, { double.NaN, double.NaN } });

        Assert.True(double.IsNaN(HeatmapRasterizer.Sample(grid, new Vec2(1, 1))));
    }

    [Fact]
    public void Image_covers_the_area_with_square_pixels()
    {
        HeatmapImage image = HeatmapRasterizer.Render(TwoByTwo(), 0.25, 153);

        Assert.Equal(8, image.Width);
        Assert.Equal(8, image.Height);
        Assert.Equal(new Vec2(0, 0), image.Origin);
        Assert.Equal(8 * 8 * 4, image.Bgra.Length);
    }

    [Fact]
    public void Pixel_colour_follows_the_continuous_ramp_and_row_zero_is_the_top()
    {
        HeatmapImage image = HeatmapRasterizer.Render(TwoByTwo(), 0.25, 153);

        Rgb topLeft = SignalScale.RampColor(-60);
        Rgb bottomRight = SignalScale.RampColor(-70);
        Assert.Equal((topLeft.R, topLeft.G, topLeft.B, (byte)153), image.Pixel(0, 0));
        Assert.Equal((bottomRight.R, bottomRight.G, bottomRight.B, (byte)153), image.Pixel(7, 7));
    }

    [Fact]
    public void Pixels_outside_the_area_are_transparent()
    {
        var values = new double[4, 4];
        for (int c = 0; c < 4; c++) for (int r = 0; r < 4; r++) values[c, r] = -55;
        var lShape = new Polygon2([new Vec2(0, 0), new Vec2(4, 0), new Vec2(4, 2), new Vec2(2, 2), new Vec2(2, 4), new Vec2(0, 4)]);

        HeatmapImage image = HeatmapRasterizer.Render(Grid(values, lShape), 0.5, 153);

        Assert.Equal(0, image.Pixel(6, 1).A);
        Assert.Equal(153, image.Pixel(1, 1).A);
        Assert.Equal(153, image.Pixel(6, 6).A);
        Assert.Equal(0, image.Pixel(4, 3).A);
        Assert.Equal(153, image.Pixel(3, 3).A);
    }

    [Fact]
    public void Neighbouring_pixels_change_gradually()
    {
        var values = new double[45, 1];
        for (int c = 0; c < 45; c++) values[c, 0] = -48 - c;

        HeatmapImage image = HeatmapRasterizer.Render(Grid(values), 0.25, 153);

        for (int x = 1; x < image.Width; x++)
        {
            var a = image.Pixel(x - 1, 0);
            var b = image.Pixel(x, 0);
            Assert.InRange(Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B), 0, 40);
        }
    }

    [Fact]
    public void Pixel_size_is_at_most_a_quarter_of_the_cell()
    {
        var spec = new GridSpec(new Vec2(0, 0), 0.5, 60, 28);
        var extent = new BoundingBox(new Vec2(0, 0), new Vec2(30, 14));

        double size = HeatmapRasterizer.PixelSizeFor(spec, extent);

        Assert.True(size <= 0.125);
        Assert.Equal(30.0 / HeatmapRasterizer.TargetLongSidePx, size, 9);
    }

    [Fact]
    public void Pixel_count_is_capped_for_huge_areas()
    {
        var spec = new GridSpec(new Vec2(0, 0), 0.5, 2000, 1000);
        var extent = new BoundingBox(new Vec2(0, 0), new Vec2(1000, 500));

        double size = HeatmapRasterizer.PixelSizeFor(spec, extent);

        Assert.True(1000 / size * (500 / size) <= HeatmapRasterizer.MaxPixels * 1.0001);
    }
}
