using System.Text.Json;

namespace WifiHeat.Core.Materials;

/// <summary>
/// Lee el catálogo JSON (config/materials.json). Rechaza pérdidas faltantes, null o negativas:
/// null nunca equivale a 0 dB.
/// </summary>
public static class MaterialCatalogParser
{
    public const string Key2_4 = "2.4GHz";
    public const string Key5 = "5GHz";

    public static MaterialCatalog Parse(string json)
    {
        var errors = new List<string>();
        var materials = new List<Material>();

        using JsonDocument document = JsonDocument.Parse(json);
        if (!document.RootElement.TryGetProperty("materials", out JsonElement list) || list.ValueKind != JsonValueKind.Array)
        {
            throw new MaterialCatalogException(["Falta el arreglo 'materials'."]);
        }

        int index = 0;
        foreach (JsonElement item in list.EnumerateArray())
        {
            string where = $"materials[{index++}]";
            string? id = ReadString(item, "id");
            string? name = ReadString(item, "name");
            if (string.IsNullOrWhiteSpace(id)) { errors.Add($"{where}: falta 'id'."); continue; }
            where = $"material '{id}'";
            if (string.IsNullOrWhiteSpace(name)) { errors.Add($"{where}: falta 'name'."); }

            double? loss24 = null, loss5 = null;
            if (item.TryGetProperty("lossDb", out JsonElement loss) && loss.ValueKind == JsonValueKind.Object)
            {
                loss24 = ReadLoss(loss, Key2_4, where, errors);
                loss5 = ReadLoss(loss, Key5, where, errors);
            }
            else
            {
                errors.Add($"{where}: falta el objeto 'lossDb'.");
            }

            if (loss24 is double a && loss5 is double b && !string.IsNullOrWhiteSpace(name))
            {
                materials.Add(new Material(id, name, a, b));
            }
        }

        if (errors.Count > 0)
        {
            throw new MaterialCatalogException(errors);
        }

        return new MaterialCatalog(materials);
    }

    private static string? ReadString(JsonElement item, string property) =>
        item.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static double? ReadLoss(JsonElement loss, string key, string where, List<string> errors)
    {
        if (!loss.TryGetProperty(key, out JsonElement value) || value.ValueKind != JsonValueKind.Number)
        {
            errors.Add($"{where}: pérdida '{key}' faltante o null (null no equivale a 0 dB).");
            return null;
        }

        double db = value.GetDouble();
        if (db < 0 || double.IsNaN(db) || double.IsInfinity(db))
        {
            errors.Add($"{where}: pérdida '{key}' inválida ({db}); debe ser un número >= 0.");
            return null;
        }

        return db;
    }
}

public sealed class MaterialCatalogException(IReadOnlyList<string> errors)
    : Exception("Catálogo de materiales inválido:\n - " + string.Join("\n - ", errors))
{
    public IReadOnlyList<string> Errors { get; } = errors;
}
