using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.Reading;

public sealed record DrawingWalls(IReadOnlyList<Wall> Walls, IReadOnlyList<ValidationIssue> Issues, DrawingUnit? Unit);

/// <summary>Convierte las entidades de la capa de muros en muros de Core, en metros.</summary>
public static class WallReader
{
    public static DrawingWalls Read(Database db)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        DrawingWalls result = Read(db, tr);
        tr.Commit();
        return result;
    }

    public static DrawingWalls Read(Database db, Transaction tr)
    {
        var walls = new List<Wall>();
        var issues = new List<ValidationIssue>();

        DrawingUnit? unit = DrawingStore.ReadUnit(db, tr);
        if (unit is null) issues.Add(DrawingIssues.MissingUnit());
        DrawingUnit effective = unit ?? DrawingUnit.Metres;

        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
            if (!string.Equals(entity.Layer, WifiHeatNames.WallLayer, StringComparison.OrdinalIgnoreCase)) continue;

            string handle = entity.Handle.ToString();
            List<Segment2>? segments = entity switch
            {
                Line line => [Segment(effective, line.StartPoint, line.EndPoint)],
                Polyline polyline => PolylineSegments(effective, polyline),
                _ => null,
            };

            if (segments is null)
            {
                issues.Add(entity is Polyline
                    ? DrawingIssues.PolylineWithArcs(handle)
                    : DrawingIssues.UnsupportedEntity(handle, entity.GetRXClass().DxfName));
                continue;
            }

            (string? materialId, double? thicknessM) = WifiXData.ReadWall(entity);
            walls.Add(new Wall(handle, segments, materialId, thicknessM));
        }

        return new DrawingWalls(walls, issues, unit);
    }

    private static List<Segment2>? PolylineSegments(DrawingUnit unit, Polyline polyline)
    {
        int vertices = polyline.NumberOfVertices;
        int count = polyline.Closed ? vertices : vertices - 1;
        var segments = new List<Segment2>(Math.Max(count, 0));
        for (int i = 0; i < count; i++)
        {
            if (polyline.GetBulgeAt(i) != 0) return null;
            segments.Add(Segment(unit, polyline.GetPoint3dAt(i), polyline.GetPoint3dAt((i + 1) % vertices)));
        }

        return segments;
    }

    private static Segment2 Segment(DrawingUnit unit, Point3d a, Point3d b) =>
        new(DrawingUnits.ToMetres(unit, a.X, a.Y), DrawingUnits.ToMetres(unit, b.X, b.Y));
}
