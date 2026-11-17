using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;
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
        Assert.True(c.TryGet(c.Columns.MaterialId, out Material concrete));
        Assert.Equal("concrete", concrete.Id);
        Assert.Equal([2.5, 5, 7.5], c.Columns.Levels.Select(l => l.AdditionalLossDb));
    }

    [Fact]
    public void Without_the_columns_section_the_default_convention_applies()
    {
        MaterialCatalog c = MaterialCatalogParser.Parse("""{"materials":[{"id":"glass","name":"Vidrio","lossDb":{"2.4GHz":3,"5GHz":3.5}}]}""");

        Assert.Equal("concrete", c.Columns.MaterialId);
        Assert.Equal(2.5, c.Columns.AdditionalLossDb(Reinforcement.Low));
        Assert.Equal(5, c.Columns.AdditionalLossDb(Reinforcement.Medium));
        Assert.Equal(7.5, c.Columns.AdditionalLossDb(Reinforcement.High));
        Assert.Contains("4 barras Ø20", c.Columns.Get(Reinforcement.Low).Example);
    }

    [Fact]
    public void The_columns_section_sets_material_and_additionals()
    {
        MaterialCatalog c = MaterialCatalogParser.Parse(
            """
            {"materials":[{"id":"rc","name":"Hormigón","lossDb":{"2.4GHz":20,"5GHz":40}}],
             "columnReinforcement":{"material":"rc","levels":[
               {"id":"low","additionalLossDb":1},{"id":"medium","name":"Intermedia","additionalLossDb":2},{"id":"high","additionalLossDb":3}]}}
            """);

        Assert.Equal("rc", c.Columns.MaterialId);
        Assert.Equal([1.0, 2, 3], c.Columns.Levels.Select(l => l.AdditionalLossDb));
        Assert.Equal("Intermedia", c.Columns.Get(Reinforcement.Medium).Name);
    }

    [Theory]
    [InlineData("""{"material":"nope","levels":[{"id":"low","additionalLossDb":1},{"id":"medium","additionalLossDb":2},{"id":"high","additionalLossDb":3}]}""", "nope")]
    [InlineData("""{"material":"rc","levels":[{"id":"low","additionalLossDb":1},{"id":"medium","additionalLossDb":2}]}""", "high")]
    [InlineData("""{"material":"rc","levels":[{"id":"low","additionalLossDb":-1},{"id":"medium","additionalLossDb":2},{"id":"high","additionalLossDb":3}]}""", "low")]
    [InlineData("""{"material":"rc","levels":[{"id":"huge","additionalLossDb":1}]}""", "huge")]
    [InlineData("""{"material":"rc"}""", "levels")]
    public void An_invalid_columns_section_is_rejected(string section, string expectedInError)
    {
        string json = """{"materials":[{"id":"rc","name":"Hormigón","lossDb":{"2.4GHz":20,"5GHz":40}}],"columnReinforcement":""" + section + "}";

        var ex = Assert.Throws<MaterialCatalogException>(() => MaterialCatalogParser.Parse(json));

        Assert.Contains(ex.Errors, e => e.Contains(expectedInError));
    }
}
