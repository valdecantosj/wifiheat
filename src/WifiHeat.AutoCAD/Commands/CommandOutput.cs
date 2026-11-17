using Autodesk.AutoCAD.EditorInput;

namespace WifiHeat.AutoCAD.Commands;

/// <summary>Formato común de errores, avisos y preguntas en la línea de comandos.</summary>
public static class CommandOutput
{
    private const string YesKeyword = "Si";
    private const string NoKeyword = "No";

    public static void Problems(Editor ed, string command, IReadOnlyList<string> errors, IReadOnlyList<string> warnings)
    {
        foreach (string error in errors) ed.WriteMessage($"\n{command}: {error}");
        foreach (string warning in warnings) ed.WriteMessage($"\n  [AVISO] {warning}");
        if (errors.Count > 0) ed.WriteMessage("\n");
    }

    /// <summary>Pregunta [Sí/No]; Enter toma el valor por defecto. Devuelve false si el usuario cancela.</summary>
    public static bool AskYesNo(Editor ed, string prompt, bool defaultYes, out bool yes)
    {
        var options = new PromptKeywordOptions(prompt) { AllowNone = true };
        options.Keywords.Add(YesKeyword, YesKeyword, "Sí");
        options.Keywords.Add(NoKeyword);
        options.Keywords.Default = defaultYes ? YesKeyword : NoKeyword;
        PromptResult answer = ed.GetKeywords(options);
        yes = answer.Status == PromptStatus.None ? defaultYes : answer.StringResult == YesKeyword;
        return answer.Status is PromptStatus.OK or PromptStatus.None;
    }
}
