using WifiHeat.Core.Geometry;

namespace WifiHeat.AutoCAD.UI.ViewModels;

/// <summary>
/// Lo que el panel necesita del editor del dibujo: pedir puntos y entidades, mostrar entidades y elegir un archivo.
/// Puntos en metros, coordenadas universales. Las preguntas devuelven null si el usuario cancela.
/// </summary>
public interface IDrawingInteraction
{
    /// <summary>Motivo por el que no se puede usar el dibujo ahora (por ejemplo, hay un comando en curso); null si está libre.</summary>
    string? BusyReason { get; }

    Task<Vec2?> PickPointAsync(string prompt);

    Task<BoundingBox?> PickWindowAsync(string prompt);

    /// <summary>
    /// Handles de las líneas y polilíneas elegidas (y de los hatches, si se piden). Si ya hay una selección en el dibujo, la usa sin preguntar.
    /// </summary>
    Task<IReadOnlyList<string>?> PickEntitiesAsync(string prompt, bool includeHatches = false);

    Task<string?> PickPolylineAsync(string prompt);

    /// <summary>Cancela la pregunta pendiente, como Esc: la pregunta devuelve null.</summary>
    void CancelPrompt();

    /// <summary>Deja seleccionadas las entidades en el dibujo y, si se pide, encuadra la vista en ellas.</summary>
    void Show(IReadOnlyCollection<string> handles, bool zoom);

    string? AskSavePath(string suggestedPath);

    /// <summary>Cede el control para que la interfaz se redibuje antes de una operación larga.</summary>
    Task YieldAsync();
}
