namespace WifiHeat.Core.Workflow;

/// <summary>Problemas que detecta la lectura del dibujo antes de validar los muros.</summary>
public static class DrawingIssues
{
    public static ValidationIssue MissingUnit() =>
        new(IssueSeverity.Error, ValidationIssue.Units, null, "El dibujo no tiene unidad guardada. Ejecutar WIFISETUP.");

    public static ValidationIssue PolylineWithArcs(string entityRef) =>
        new(IssueSeverity.Error, ValidationIssue.InvalidGeometry, entityRef,
            "La polilínea tiene tramos en arco. Un muro admite solo tramos rectos.");

    public static ValidationIssue UnsupportedEntity(string entityRef, string kind) =>
        new(IssueSeverity.Warning, ValidationIssue.InvalidGeometry, entityRef,
            $"Entidad {kind} en la capa de muros: se ignora. Un muro es una línea o una polilínea.");
}
