using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using WifiHeat.AutoCAD.Rendering;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;

namespace WifiHeat.AutoCAD.Reading;

/// <summary>Figuras cerradas del plano que pueden ser columnas: hatches de un solo contorno recto y polilíneas cerradas.</summary>
public static class ColumnReader
{
    private const double ClosedToleranceM = 1e-6;

    /// <summary>Contorno de la entidad en metros, o el motivo por el que no sirve como columna.</summary>
    public static ClosedFigure Read(Entity entity, DrawingUnit unit)
    {
        string handle = entity.Handle.ToString();
        string kind = entity.GetRXClass().DxfName;
        bool isColumn = string.Equals(entity.Layer, WifiHeatNames.WallLayer, StringComparison.OrdinalIgnoreCase) && WifiXData.IsColumn(entity);

        Polygon2? polygon = null;
        string? error;
        switch (entity)
        {
            case Hatch hatch:
                polygon = HatchContour(hatch, unit, out error);
                break;
            case Polyline polyline:
                AreaReader.TryPolygon(polyline, unit, out polygon, out error);
                break;
            default:
                error = "no es un hatch ni una polilínea cerrada";
                break;
        }

        return new ClosedFigure(handle, kind, polygon, error, isColumn);
    }

    /// <summary>Hatches y polilíneas cerradas con todos sus vértices dentro de la zona, fuera de las capas propias.</summary>
    public static IReadOnlyList<ClosedFigure> ReadInside(Database db, Transaction tr, DrawingUnit unit, BoundingBox window)
    {
        var figures = new List<ClosedFigure>();
        foreach (ObjectId id in ModelSpace.Open(db, tr, OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
            if (entity is not (Hatch or Polyline)) continue;
            if (entity.Layer.StartsWith("WIFIHEAT_", StringComparison.OrdinalIgnoreCase)) continue;
            if (entity is Polyline polyline && !IsClosed(polyline, unit)) continue;

            ClosedFigure figure = Read(entity, unit);
            if (figure.Polygon is null) continue;

            BoundingBox b = figure.Polygon.Bounds;
            if (b.Min.X >= window.Min.X && b.Min.Y >= window.Min.Y && b.Max.X <= window.Max.X && b.Max.Y <= window.Max.Y) figures.Add(figure);
        }

        return figures;
    }

    /// <summary>Cerrada por su marca o porque el último vértice coincide con el primero.</summary>
    public static bool IsClosed(Polyline polyline, DrawingUnit unit)
    {
        if (polyline.Closed) return true;

        int count = polyline.NumberOfVertices;
        if (count < 4) return false;

        Point2d first = polyline.GetPoint2dAt(0), last = polyline.GetPoint2dAt(count - 1);
        return Vec2.Distance(DrawingUnits.ToMetres(unit, first.X, first.Y), DrawingUnits.ToMetres(unit, last.X, last.Y)) <= ClosedToleranceM;
    }

    /// <summary>
    /// Contorno de un hatch de un solo bucle formado por tramos rectos. Los puntos del bucle están en el plano del hatch;
    /// se llevan a coordenadas universales con su normal y su elevación.
    /// </summary>
    private static Polygon2? HatchContour(Hatch hatch, DrawingUnit unit, out string? error)
    {
        if (hatch.NumberOfLoops == 0)
        {
            error = "no tiene contorno";
            return null;
        }

        if (hatch.NumberOfLoops > 1)
        {
            error = "tiene islas o más de un contorno";
            return null;
        }

        var points = new List<Point2d>();
        HatchLoop loop = hatch.GetLoopAt(0);
        if (loop.IsPolyline)
        {
            foreach (BulgeVertex vertex in loop.Polyline)
            {
                if (vertex.Bulge != 0)
                {
                    error = "tiene arcos (solo se admiten tramos rectos)";
                    return null;
                }

                points.Add(vertex.Vertex);
            }
        }
        else
        {
            foreach (Curve2d curve in loop.Curves)
            {
                if (curve is not LineSegment2d line)
                {
                    error = "tiene arcos o curvas (solo se admiten tramos rectos)";
                    return null;
                }

                points.Add(line.StartPoint);
            }
        }

        Matrix3d toWorld = Matrix3d.PlaneToWorld(hatch.Normal);
        try
        {
            error = null;
            return new Polygon2(points.Select(p =>
            {
                Point3d world = new Point3d(p.X, p.Y, hatch.Elevation).TransformBy(toWorld);
                return DrawingUnits.ToMetres(unit, world.X, world.Y);
            }));
        }
        catch (ArgumentException)
        {
            error = "no encierra superficie";
            return null;
        }
    }
}
