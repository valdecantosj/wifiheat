using System.Numerics;
using WifiHeat.Core.Propagation;
using Xunit;

namespace WifiHeat.Core.Tests.Propagation;

public class FresnelTests
{
    private static readonly Dielectric Concrete = new(5.24, 0, 0.0462, 0.7822);

    [Fact]
    public void Normal_incidence_on_a_lossless_material_gives_the_classic_value()
    {
        Complex gamma = Fresnel.ReflectionTe(new Complex(4, 0), 1.0);

        Assert.Equal(-1.0 / 3, gamma.Real, 12);
        Assert.Equal(0, gamma.Imaginary, 12);
        Assert.Equal(20 * Math.Log10(3), Fresnel.ReflectionLossDb(new Complex(4, 0), 1.0), 9);
    }

    [Fact]
    public void Oblique_incidence_on_a_lossless_material_follows_snell()
    {
        double cos = Math.Cos(Math.PI / 4);
        double root = Math.Sqrt(4 - 0.5);
        double expected = -20 * Math.Log10(Math.Abs((cos - root) / (cos + root)));

        Assert.Equal(expected, Fresnel.ReflectionLossDb(new Complex(4, 0), cos), 9);
        Assert.Equal(6.908, expected, 0.001);
    }

    [Fact]
    public void Grazing_incidence_reflects_everything()
    {
        Assert.Equal(0, Fresnel.ReflectionLossDb(Concrete, 2437, 0.0), 9);
    }

    [Fact]
    public void The_loss_drops_as_the_incidence_becomes_grazing()
    {
        double previous = double.PositiveInfinity;
        for (int degrees = 0; degrees <= 89; degrees += 5)
        {
            double loss = Fresnel.ReflectionLossDb(Concrete, 5500, Math.Cos(degrees * Math.PI / 180));
            Assert.True(loss < previous);
            previous = loss;
        }
    }

    [Fact]
    public void Concrete_permittivity_follows_the_itu_fit()
    {
        Complex eps = Concrete.ComplexPermittivity(2.4);

        Assert.Equal(5.24, eps.Real, 12);
        Assert.Equal(0.0462 * Math.Pow(2.4, 0.7822), Concrete.ConductivitySPerM(2.4), 12);
        Assert.Equal(-17.98 * Concrete.ConductivitySPerM(2.4) / 2.4, eps.Imaginary, 12);
        Assert.Equal(0.6865, -eps.Imaginary, 0.001);
    }

    [Theory]
    [InlineData(2400, 8.07)]
    [InlineData(5500, 8.09)]
    public void Normal_incidence_on_concrete(double frequencyMHz, double expectedDb)
    {
        Assert.Equal(expectedDb, Fresnel.ReflectionLossDb(Concrete, frequencyMHz, 1.0), 0.01);
    }
}
