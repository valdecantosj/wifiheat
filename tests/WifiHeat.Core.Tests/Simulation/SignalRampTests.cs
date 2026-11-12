using WifiHeat.Core.Simulation;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class SignalRampTests
{
    [Theory]
    [InlineData(-50, 0)]
    [InlineData(-60, 1)]
    [InlineData(-67, 2)]
    [InlineData(-75, 3)]
    [InlineData(-85, 4)]
    [InlineData(SignalScale.RedAnchorDbm, 5)]
    public void Anchor_values_get_the_exact_legend_color(double dbm, int levelIndex)
    {
        SignalLevel level = SignalScale.Levels[levelIndex];

        Assert.Equal(new Rgb(level.R, level.G, level.B), SignalScale.RampColor(dbm));
    }

    [Theory]
    [InlineData(-55, 60, 164, 40)]
    [InlineData(-63.5, 155, 210, 60)]
    [InlineData(-71, 223, 220, 20)]
    [InlineData(-80, 255, 180, 0)]
    [InlineData(-90, 238, 85, 15)]
    public void Midpoints_mix_the_neighbouring_colors(double dbm, byte r, byte g, byte b)
    {
        Assert.Equal(new Rgb(r, g, b), SignalScale.RampColor(dbm));
    }

    [Fact]
    public void Quarter_of_an_interval_is_linear()
    {
        Assert.Equal(new Rgb(30, 146, 20), SignalScale.RampColor(-52.5));
    }

    [Theory]
    [InlineData(-20)]
    [InlineData(-49.999)]
    public void Above_the_top_cut_is_dark_green(double dbm)
    {
        Assert.Equal(new Rgb(0, 128, 0), SignalScale.RampColor(dbm));
    }

    [Theory]
    [InlineData(-95.001)]
    [InlineData(-150)]
    [InlineData(double.NegativeInfinity)]
    public void Below_the_red_anchor_is_red(double dbm)
    {
        Assert.Equal(new Rgb(220, 30, 30), SignalScale.RampColor(dbm));
    }

    [Fact]
    public void Ramp_is_continuous_across_a_cut()
    {
        Rgb above = SignalScale.RampColor(-74.999);
        Rgb below = SignalScale.RampColor(-75.001);

        Assert.InRange(Math.Abs(above.R - below.R) + Math.Abs(above.G - below.G) + Math.Abs(above.B - below.B), 0, 1);
    }

    [Fact]
    public void Classes_are_unchanged()
    {
        Assert.Equal("Naranja", SignalScale.Classify(-80).ColorName);
        Assert.Equal("Rojo", SignalScale.Classify(-90).ColorName);
    }

    [Fact]
    public void Not_a_number_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => SignalScale.RampColor(double.NaN));
    }
}
