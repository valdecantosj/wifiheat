namespace WifiHeat.Core.Materials;

/// <summary>Catálogo de materiales indexado por id (sin distinguir mayúsculas), con la convención de armadura de las columnas.</summary>
public sealed class MaterialCatalog
{
    private readonly Dictionary<string, Material> _byId;

    public MaterialCatalog(IEnumerable<Material> materials, ColumnReinforcement? columns = null)
    {
        _byId = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        foreach (Material material in materials)
        {
            if (!_byId.TryAdd(material.Id, material))
            {
                throw new ArgumentException($"Material duplicado: '{material.Id}'.", nameof(materials));
            }
        }

        Columns = columns ?? ColumnReinforcement.Default;
    }

    public IReadOnlyCollection<Material> All => _byId.Values;

    public ColumnReinforcement Columns { get; }

    public bool TryGet(string id, out Material material) => _byId.TryGetValue(id, out material!);
}
