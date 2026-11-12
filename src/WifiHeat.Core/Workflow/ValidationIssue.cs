namespace WifiHeat.Core.Workflow;

public enum IssueSeverity
{
    Error,
    Warning,
}

/// <summary>
/// Un problema de validación. EntityRef identifica qué corregir (por ejemplo, el handle del muro en el DWG).
/// </summary>
public sealed record ValidationIssue(IssueSeverity Severity, string Code, string? EntityRef, string Message)
{
    public const string MissingProperty = "FALTA PROPIEDAD";
    public const string UnknownMaterial = "MATERIAL DESCONOCIDO";
    public const string InvalidGeometry = "GEOMETRIA INVALIDA";
    public const string NoWalls = "SIN MUROS";
    public const string Units = "UNIDADES";
    public const string LargeArea = "AREA GRANDE";

    public override string ToString() =>
        $"[{(Severity == IssueSeverity.Error ? "ERROR" : "AVISO")}] {Code}{(EntityRef is null ? "" : $" ({EntityRef})")}: {Message}";
}

public sealed class ValidationReport(IReadOnlyList<ValidationIssue> issues)
{
    public IReadOnlyList<ValidationIssue> Issues { get; } = issues;

    public bool IsValid => Issues.All(i => i.Severity != IssueSeverity.Error);

    public int ErrorCount => Issues.Count(i => i.Severity == IssueSeverity.Error);

    public int WarningCount => Issues.Count(i => i.Severity == IssueSeverity.Warning);
}
