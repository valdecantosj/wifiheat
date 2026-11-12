namespace WifiHeat.Core.Materials;

/// <summary>Catálogo de materiales indexado por id (sin distinguir mayúsculas).</summary>
public sealed class MaterialCatalog
{
    private readonly Dictionary<string, Material> _byId;

    public MaterialCatalog(IEnumerable<Material> materials)
    {
        _byId = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        foreach (Material material in materials)
        {
            if (!_byId.TryAdd(material.Id, material))
            {
                throw new ArgumentException($"Material duplicado: '{material.Id}'.", nameof(materials));
            }
        }
    }

    public IReadOnlyCollection<Material> All => _byId.Values;

    public bool TryGet(string id, out Material material) => _byId.TryGetValue(id, out material!);
}
