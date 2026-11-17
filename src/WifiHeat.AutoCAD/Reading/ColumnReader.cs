using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using WifiHeat.AutoCAD.Rendering;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;

namespace WifiHeat.AutoCAD.Reading;

/// <summary>
/// Figuras cerradas del plano que pueden ser columnas: polilíneas cerradas y hatches de contornos rectos.
/// Un hatch con varios contornos separados da una figura por contorno; con un contorno dentro de otro (islas) o con arcos, ninguna.
/// </summary>
public static class ColumnReader
{
    private const double ClosedToleranceM = 1e-6;

    /// <summary>Contornos de la entidad en metros, o una sola figura sin contorno con el motivo por el que no sirve.</summary>
    public static IReadOnlyList<ClosedFigure> Read(Entity entity, DrawingUnit unit)
    {
        string handle = entity.Handle.ToString();
        string kind = entity.GetRXClass().DxfName;
        bool isColumn = string.Equals(entity.Layer, WifiHeatNames.WallLayer, StringComparison.OrdinalIgnoreCase) && WifiXData.IsColumn(entity);

        string? error;
        switch (entity)
        {
            case Hatch hatch:
                List<Polygon2>? contours = HatchContours(hatch, unit, out error);
                if (contours is not null) return [.. contours.Select(c => new ClosedFigure(handle, kind, c, null, isColumn))];
                break;
            case Polyline polyline:
                if (AreaReader.TryPolygon(polyline, unit, out Polygon2? polygon, out error)) return [new ClosedFigure(handle, kind, polygon, null, isColumn)];
                break;
            default:
                error = "no es un hatch ni una polilínea cerrada";
                break;
        }

        return [new ClosedFigure(handle, kind, null, error, isColumn)];
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

            foreach (ClosedFigure figure in Read(entity, unit))
            {
                if (figure.Polygon is null) continue;

                BoundingBox b = figure.Polygon.Bounds;
                if (b.Min.X >= window.Min.X && b.Min.Y >= window.Min.Y && b.Max.X <= window.Max.X && b.Max.Y <= window.Max.Y) figures.Add(figure);
            }
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
    /// Contornos de un hatch formado solo por tramos rectos. Los puntos de cada bucle están en el plano del hatch;
    /// se llevan a coordenadas universales con su normal y su elevación.
    /// </summary>
    private static List<Polygon2>? HatchContours(Hatch hatch, DrawingUnit unit, out string? error)
    {
        if (hatch.NumberOfLoops == 0)
        {
            error = "no tiene contorno";
            return null;
        }

        Matrix3d toWorld = Matrix3d.PlaneToWorld(hatch.Normal);
        var contours = new List<Polygon2>(hatch.NumberOfLoops);
        for (int i = 0; i < hatch.NumberOfLoops; i++)
        {
            List<Point2d>? points = LoopPoints(hatch.GetLoopAt(i));
            if (points is null)
            {
                error = "tiene arcos o curvas (solo se admiten tramos rectos)";
                return null;
            }

            try
            {
                contours.Add(new Polygon2(points.Select(p =>
                {
                    Point3d world = new Point3d(p.X, p.Y, hatch.Elevation).TransformBy(toWorld);
                    return DrawingUnits.ToMetres(unit, world.X, world.Y);
                })));
            }
            catch (ArgumentException)
            {
                error = "tiene un contorno que no encierra superficie";
                return null;
            }
        }

        for (int i = 0; i < contours.Count; i++)
        {
            for (int j = 0; j < contours.Count; j++)
            {
                if (i == j || !contours[j].Contains(contours[i].Vertices[0])) continue;

                error = "tiene islas (un contorno dentro de otro)";
                return null;
            }
        }

        error = null;
        return contours;
    }

    private static List<Point2d>? LoopPoints(HatchLoop loop)
    {
        var points = new List<Point2d>();
        if (loop.IsPolyline)
        {
            foreach (BulgeVertex vertex in loop.Polyline)
            {
                if (vertex.Bulge != 0) return null;
                points.Add(vertex.Vertex);
            }
        }
        else
        {
            foreach (Curve2d curve in loop.Curves)
            {
                if (curve is not LineSegment2d line) return null;
                points.Add(line.StartPoint);
            }
        }

        return points;
    }
}
