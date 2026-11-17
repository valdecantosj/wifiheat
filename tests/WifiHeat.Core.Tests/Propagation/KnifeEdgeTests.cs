using WifiHeat.Core.Propagation;
using WifiHeat.Core.Tests.Data;
using Xunit;

namespace WifiHeat.Core.Tests.Propagation;

public class KnifeEdgeTests
{
    [Fact]
    public void Loss_matches_the_reference_table()
    {
        Assert.Equal(11, ReferenceData.KnifeEdge.Count);
        foreach ((double nu, double lossDb) in ReferenceData.KnifeEdge)
        {
            Assert.Equal(lossDb, KnifeEdge.LossDb(nu), 0.001);
        }
    }

    [Fact]
    public void Loss_at_grazing_incidence_is_about_six_decibels()
    {
        Assert.Equal(6.0329, KnifeEdge.J0, 0.0001);
        Assert.Equal(KnifeEdge.J0, KnifeEdge.LossDb(0), 12);
    }

    [Theory]
    [InlineData(-0.78)]
    [InlineData(-0.9)]
    [InlineData(-50)]
    public void There_is_no_loss_at_or_below_the_cutoff(double nu)
    {
        Assert.Equal(0, KnifeEdge.LossDb(nu));
    }

    [Fact]
    public void Loss_grows_with_nu()
    {
        double previous = KnifeEdge.LossDb(-0.77);
        for (double nu = -0.7; nu <= 12; nu += 0.1)
        {
            double loss = KnifeEdge.LossDb(nu);
            Assert.True(loss > previous);
            previous = loss;
        }
    }

    [Fact]
    public void Nu_follows_the_angular_form()
    {
        double lambda = KnifeEdge.WavelengthM(5180);

        Assert.Equal(299.792458 / 5180, lambda, 12);
        Assert.Equal(0.1 * Math.Sqrt(2 * 3.0 * 3.0 / (lambda * 6.0)), KnifeEdge.NuFromAngle(0.1, 3, 3, lambda), 12);
        Assert.Equal(0.72, KnifeEdge.NuFromAngle(0.1, 3, 3, lambda), 0.001);
    }
}
