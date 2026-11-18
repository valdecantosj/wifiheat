using System.Text.Json;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;

namespace WifiHeat.Core.Materials;

/// <summary>
/// Lee el catálogo JSON (config/materials.json). Rechaza pérdidas faltantes, null o negativas:
/// null nunca equivale a 0 dB. La sección «columnReinforcement» es opcional; si falta se usa la convención por defecto.
/// «reflection» es opcional en cada material: coeficientes a, b, c y d de ITU-R P.2040 (ε′ = a·f^b, σ = c·f^d, f en GHz).
/// </summary>
public static class MaterialCatalogParser
{
    public const string Key2_4 = "2.4GHz";
    public const string Key5 = "5GHz";
    public const string ColumnsKey = "columnReinforcement";
    public const string ReflectionKey = "reflection";

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

            Dielectric? reflection = null;
            bool reflectionOk = true;
            if (item.TryGetProperty(ReflectionKey, out JsonElement electric))
            {
                reflection = ReadReflection(electric, where, errors);
                reflectionOk = reflection is not null;
            }

            if (loss24 is double a && loss5 is double b && !string.IsNullOrWhiteSpace(name) && reflectionOk)
            {
                materials.Add(new Material(id, name, a, b, reflection));
            }
        }

        ColumnReinforcement? columns = null;
        if (document.RootElement.TryGetProperty(ColumnsKey, out JsonElement section))
        {
            columns = ReadColumns(section, materials, errors);
        }

        if (errors.Count > 0)
        {
            throw new MaterialCatalogException(errors);
        }

        return new MaterialCatalog(materials, columns);
    }

    private static ColumnReinforcement? ReadColumns(JsonElement section, List<Material> materials, List<string> errors)
    {
        if (section.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"'{ColumnsKey}' debe ser un objeto.");
            return null;
        }

        string materialId = ReadString(section, "material") ?? ColumnReinforcement.DefaultMaterialId;
        if (!materials.Any(m => string.Equals(m.Id, materialId, StringComparison.OrdinalIgnoreCase)))
        {
            errors.Add($"{ColumnsKey}: el material '{materialId}' no está en 'materials'.");
        }

        if (!section.TryGetProperty("levels", out JsonElement levels) || levels.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{ColumnsKey}: falta el arreglo 'levels'.");
            return null;
        }

        var read = new Dictionary<Reinforcement, ReinforcementLevel>();
        foreach (JsonElement item in levels.EnumerateArray())
        {
            string? id = ReadString(item, "id");
            if (!ColumnReinforcement.TryParseId(id, out Reinforcement level))
            {
                errors.Add($"{ColumnsKey}: nivel desconocido '{id}'. Valores posibles: {string.Join(", ", ColumnReinforcement.Default.Levels.Select(l => l.Id))}.");
                continue;
            }

            if (!item.TryGetProperty("additionalLossDb", out JsonElement value) || value.ValueKind != JsonValueKind.Number ||
                value.GetDouble() is var db && (db < 0 || !double.IsFinite(db)))
            {
                errors.Add($"{ColumnsKey}: el nivel '{id}' necesita 'additionalLossDb' como número >= 0.");
                continue;
            }

            ReinforcementLevel known = ColumnReinforcement.Default.Get(level);
            if (!read.TryAdd(level, known with
                {
                    Name = ReadString(item, "name") ?? known.Name,
                    Example = ReadString(item, "example") ?? known.Example,
                    AdditionalLossDb = value.GetDouble(),
                }))
            {
                errors.Add($"{ColumnsKey}: el nivel '{id}' está repetido.");
            }
        }

        foreach (ReinforcementLevel known in ColumnReinforcement.Default.Levels)
        {
            if (!read.ContainsKey(known.Level) && errors.Count == 0) errors.Add($"{ColumnsKey}: falta el nivel '{known.Id}'.");
        }

        return errors.Count > 0 ? null : new ColumnReinforcement(materialId, [.. read.Values.OrderBy(l => l.Level)]);
    }

    private static Dielectric? ReadReflection(JsonElement section, string where, List<string> errors)
    {
        if (section.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{where}: '{ReflectionKey}' debe ser un objeto con 'a', 'b', 'c' y 'd'.");
            return null;
        }

        double? a = ReadCoefficient(section, "a", where, errors);
        double? b = ReadCoefficient(section, "b", where, errors);
        double? c = ReadCoefficient(section, "c", where, errors);
        double? d = ReadCoefficient(section, "d", where, errors);
        if (a is not { } va || b is not { } vb || c is not { } vc || d is not { } vd) return null;

        if (va < 1)
        {
            errors.Add($"{where}: '{ReflectionKey}.a' inválido ({va}); la permitividad relativa debe ser >= 1.");
            return null;
        }

        if (vc < 0)
        {
            errors.Add($"{where}: '{ReflectionKey}.c' inválido ({vc}); la conductividad debe ser >= 0.");
            return null;
        }

        return new Dielectric(va, vb, vc, vd);
    }

    private static double? ReadCoefficient(JsonElement section, string key, string where, List<string> errors)
    {
        if (!section.TryGetProperty(key, out JsonElement value) || value.ValueKind != JsonValueKind.Number || !double.IsFinite(value.GetDouble()))
        {
            errors.Add($"{where}: falta '{ReflectionKey}.{key}' o no es un número.");
            return null;
        }

        return value.GetDouble();
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
