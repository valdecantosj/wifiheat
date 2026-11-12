using WifiHeat.Core.Materials;
using Xunit;

namespace WifiHeat.Core.Tests.Materials;

public class MaterialCatalogParserTests
{
    [Fact]
    public void Parses_valid_material()
    {
        MaterialCatalog c = MaterialCatalogParser.Parse("""{"materials":[{"id":"glass","name":"Vidrio","lossDb":{"2.4GHz":3,"5GHz":3.5}}]}""");

        Assert.True(c.TryGet("GLASS", out Material m));
        Assert.Equal(3.5, m.LossDb(Band.Ghz5), 9);
    }

    [Fact]
    public void Null_loss_is_rejected_not_treated_as_zero()
    {
        var ex = Assert.Throws<MaterialCatalogException>(() =>
            MaterialCatalogParser.Parse("""{"materials":[{"id":"x","name":"X","lossDb":{"2.4GHz":null,"5GHz":4}}]}"""));

        Assert.Contains("null", ex.Errors[0]);
    }

    [Fact]
    public void Negative_loss_is_rejected()
    {
        Assert.Throws<MaterialCatalogException>(() =>
            MaterialCatalogParser.Parse("""{"materials":[{"id":"x","name":"X","lossDb":{"2.4GHz":-1,"5GHz":4}}]}"""));
    }

    [Fact]
    public void Repository_catalog_is_valid()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "materials.json");
        MaterialCatalog c = MaterialCatalogParser.Parse(File.ReadAllText(path));

        Assert.Equal(5, c.All.Count);
    }
}
