using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;

namespace WifiHeat.Core.Workflow;

/// <summary>Valida los muros leídos del plano antes de calcular. No completa datos faltantes.</summary>
public static class SceneValidator
{
    public const double MinSegmentLengthM = 0.001;

    public static ValidationReport Validate(IReadOnlyList<Wall> walls, MaterialCatalog catalog, IEnumerable<ValidationIssue>? hostIssues = null)
    {
        var issues = new List<ValidationIssue>(hostIssues ?? []);

        if (walls.Count == 0)
        {
            issues.Add(new(IssueSeverity.Error, ValidationIssue.NoWalls, null,
                "No hay muros de simulación en la capa de muros. Dibujar ejes de muro y asignarles material."));
        }

        foreach (Wall wall in walls)
        {
            if (string.IsNullOrWhiteSpace(wall.MaterialId))
            {
                issues.Add(new(IssueSeverity.Error, ValidationIssue.MissingProperty, wall.Id, "Falta el material. Usar WIFIWALL para asignarlo."));
            }
            else if (!catalog.TryGet(wall.MaterialId, out _))
            {
                issues.Add(new(IssueSeverity.Error, ValidationIssue.UnknownMaterial, wall.Id,
                    $"El material '{wall.MaterialId}' no existe en el catálogo."));
            }

            if (wall.ThicknessM is null)
            {
                issues.Add(new(IssueSeverity.Error, ValidationIssue.MissingProperty, wall.Id, "Falta el espesor. Usar WIFIWALL para asignarlo."));
            }
            else if (wall.ThicknessM <= 0)
            {
                issues.Add(new(IssueSeverity.Error, ValidationIssue.InvalidGeometry, wall.Id, "El espesor debe ser mayor que 0."));
            }

            if (wall.Segments.Count == 0)
            {
                issues.Add(new(IssueSeverity.Error, ValidationIssue.InvalidGeometry, wall.Id, "El muro no tiene segmentos rectos."));
            }
            else if (wall.Segments.Any(s => s.Length < MinSegmentLengthM))
            {
                issues.Add(new(IssueSeverity.Warning, ValidationIssue.InvalidGeometry, wall.Id, "Tiene segmentos de longitud casi nula."));
            }
        }

        return new ValidationReport(issues);
    }
}
