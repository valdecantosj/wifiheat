using WifiHeat.Core.Simulation;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class SignalRampTests
{
    [Theory]
    [InlineData(-50, 0)]
    [InlineData(-55, 1)]
    [InlineData(-63.5, 2)]
    [InlineData(-71, 3)]
    [InlineData(-80, 4)]
    [InlineData(-90, 5)]
    public void Anchor_values_get_the_exact_legend_color(double dbm, int levelIndex)
    {
        SignalLevel level = SignalScale.Levels[levelIndex];

        Assert.Equal(dbm, level.AnchorDbm);
        Assert.Equal(new Rgb(level.R, level.G, level.B), SignalScale.RampColor(dbm));
    }

    [Fact]
    public void Each_anchor_lies_inside_its_own_class()
    {
        foreach (SignalLevel level in SignalScale.Levels)
        {
            Assert.Same(level, SignalScale.Classify(level.AnchorDbm));
        }
    }

    [Theory]
    [InlineData(-52.5, 60, 164, 40)]
    [InlineData(-59.25, 155, 210, 60)]
    [InlineData(-67.25, 223, 220, 20)]
    [InlineData(-75.5, 255, 180, 0)]
    [InlineData(-85, 238, 85, 15)]
    public void Midpoints_between_anchors_mix_the_neighbouring_colors(double dbm, byte r, byte g, byte b)
    {
        Assert.Equal(new Rgb(r, g, b), SignalScale.RampColor(dbm));
    }

    [Fact]
    public void Quarter_of_an_interval_is_linear()
    {
        Assert.Equal(new Rgb(30, 146, 20), SignalScale.RampColor(-51.25));
    }

    [Theory]
    [InlineData(-20)]
    [InlineData(-49.999)]
    public void Above_the_dark_green_anchor_saturates(double dbm)
    {
        Assert.Equal(new Rgb(0, 128, 0), SignalScale.RampColor(dbm));
    }

    [Theory]
    [InlineData(-90.001)]
    [InlineData(-150)]
    [InlineData(double.NegativeInfinity)]
    public void Below_the_red_anchor_saturates(double dbm)
    {
        Assert.Equal(new Rgb(220, 30, 30), SignalScale.RampColor(dbm));
    }

    [Theory]
    [InlineData(-55)]
    [InlineData(-60)]
    [InlineData(-71)]
    [InlineData(-75)]
    public void Ramp_is_continuous(double dbm)
    {
        Rgb above = SignalScale.RampColor(dbm + 0.001);
        Rgb below = SignalScale.RampColor(dbm - 0.001);

        Assert.InRange(Math.Abs(above.R - below.R) + Math.Abs(above.G - below.G) + Math.Abs(above.B - below.B), 0, 1);
    }

    [Theory]
    [InlineData(-50, "Verde oscuro")]
    [InlineData(-50.001, "Verde claro")]
    [InlineData(-60, "Verde claro")]
    [InlineData(-67, "Amarillo verdoso")]
    [InlineData(-75, "Amarillo")]
    [InlineData(-85, "Naranja")]
    [InlineData(-85.001, "Rojo")]
    public void Class_thresholds_are_unchanged(double dbm, string color)
    {
        Assert.Equal(color, SignalScale.Classify(dbm).ColorName);
    }

    [Fact]
    public void Not_a_number_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => SignalScale.RampColor(double.NaN));
    }
}
