using WifiHeat.Core.Simulation;

namespace WifiHeat.Core.Materials;

/// <summary>Nivel de armadura con el adicional de pérdida que suma al material de la columna (dB, igual en las dos bandas).</summary>
public sealed record ReinforcementLevel(Reinforcement Level, string Id, string Name, string Example, double AdditionalLossDb);

/// <summary>
/// Material de las columnas y adicional por nivel de armadura.
/// Los adicionales son una convención del proyecto: no hay una tabla publicada que relacione cuantía con decibeles.
/// </summary>
public sealed record ColumnReinforcement(string MaterialId, IReadOnlyList<ReinforcementLevel> Levels)
{
    public const string DefaultMaterialId = "concrete";

    public static ColumnReinforcement Default { get; } = new(DefaultMaterialId,
    [
        new(Reinforcement.Low, "low", "Baja", "4 barras Ø20 en una columna de 30 × 30 cm (cuantía ≈ 1,4 %)", 2.5),
        new(Reinforcement.Medium, "medium", "Media", "8 barras Ø20 en una columna de 30 × 30 cm (cuantía ≈ 2,8 %)", 5),
        new(Reinforcement.High, "high", "Alta", "12 barras Ø20 en una columna de 30 × 30 cm (cuantía ≈ 4,2 %)", 7.5),
    ]);

    public ReinforcementLevel Get(Reinforcement level) => Levels.First(l => l.Level == level);

    public double AdditionalLossDb(Reinforcement level) => Get(level).AdditionalLossDb;

    public static bool TryParseId(string? id, out Reinforcement level)
    {
        foreach (ReinforcementLevel known in Default.Levels)
        {
            if (!string.Equals(known.Id, id?.Trim(), StringComparison.OrdinalIgnoreCase)) continue;
            level = known.Level;
            return true;
        }

        level = default;
        return false;
    }
}
