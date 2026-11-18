namespace WifiHeat.Core.Simulation;

/// <summary>Modelo con que se estima la señal en cada punto.</summary>
public enum PropagationModel
{
    /// <summary>Trayecto directo AP–punto: L_0 más los materiales cruzados.</summary>
    StraightLine,

    /// <summary>Suma en potencia del trayecto directo y de los que rodean vértices por difracción.</summary>
    Diffraction,

    /// <summary>Además, un rebote en cada muro o cara de columna (método de las imágenes, Fresnel con ITU-R P.2040).</summary>
    DiffractionAndReflection,
}

/// <summary>
/// Topes de la búsqueda de trayectos.
/// MaxDiffractions: difracciones por trayecto (1 a 4). PruneDb: se descartan los trayectos que pierden más que el mejor en ese margen.
/// BeamWidth: llegadas que se conservan por vértice y por nivel. MaxLitPerLeg: vértices de transición por tramo.
/// </summary>
public sealed record PropagationOptions(int MaxDiffractions = 3, double PruneDb = 30, int BeamWidth = 4, int MaxLitPerLeg = 8)
{
    public const int MinDiffractions = 1;
    public const int MaxDiffractionsLimit = 4;
    public const int DefaultMaxDiffractions = 3;

    public static PropagationOptions Default { get; } = new();

    public static bool IsValidMaxDiffractions(int value) => value is >= MinDiffractions and <= MaxDiffractionsLimit;
}

public static class PropagationModelInfo
{
    /// <summary>Modelos que se pueden elegir.</summary>
    public static IReadOnlyList<PropagationModel> Available { get; } =
        [PropagationModel.StraightLine, PropagationModel.Diffraction, PropagationModel.DiffractionAndReflection];

    public static bool IsAvailable(PropagationModel model) => Available.Contains(model);

    /// <summary>Descripción de una línea, para la ayuda del selector.</summary>
    public static string Description(PropagationModel model) => model switch
    {
        PropagationModel.StraightLine => "Recta del AP a cada punto: espacio libre más los muros y columnas que cruza.",
        PropagationModel.Diffraction => "Suma la señal que rodea extremos de muro, esquinas y columnas. Tarda más en calcular.",
        PropagationModel.DiffractionAndReflection => "Además suma un rebote en cada muro y columna (Fresnel, ITU-R P.2040). Es el más lento.",
        _ => throw new ArgumentOutOfRangeException(nameof(model)),
    };

    public static string Label(PropagationModel model) => model switch
    {
        PropagationModel.StraightLine => "Línea recta",
        PropagationModel.Diffraction => "Con difracción",
        PropagationModel.DiffractionAndReflection => "Con difracción y reflexiones",
        _ => throw new ArgumentOutOfRangeException(nameof(model)),
    };
}
