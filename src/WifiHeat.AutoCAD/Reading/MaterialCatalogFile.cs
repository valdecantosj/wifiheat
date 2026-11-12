using WifiHeat.Core.Materials;

namespace WifiHeat.AutoCAD.Reading;

/// <summary>Catálogo único, leído de materials.json junto a la DLL del plugin.</summary>
public static class MaterialCatalogFile
{
    private static readonly Lazy<MaterialCatalog> Loaded = new(Load);

    public static MaterialCatalog Catalog => Loaded.Value;

    public static string FilePath =>
        Path.Combine(Path.GetDirectoryName(typeof(MaterialCatalogFile).Assembly.Location) ?? ".", "materials.json");

    private static MaterialCatalog Load() => MaterialCatalogParser.Parse(File.ReadAllText(FilePath));
}
