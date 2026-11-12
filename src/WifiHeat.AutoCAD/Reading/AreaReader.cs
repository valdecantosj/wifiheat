using Autodesk.AutoCAD.DatabaseServices;
using WifiHeat.Core.Geometry;

namespace WifiHeat.AutoCAD.Reading;

/// <summary>Área de cobertura guardada. Se lee la polilínea en cada cálculo, así que sus ediciones se respetan.</summary>
public sealed record StoredArea(Polygon2? Polygon, string? Handle, string? Warning);

public static class AreaReader
{
    public static StoredArea Read(Database db, DrawingUnit unit)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        StoredArea result = Read(db, tr, unit);
        tr.Commit();
        return result;
    }

    public static StoredArea Read(Database db, Transaction tr, DrawingUnit unit)
    {
        ObjectId? id = DrawingStore.ReadAreaId(db, tr);
        if (id is null) return new StoredArea(null, null, null);

        const string fallback = "se usa el rectángulo de los muros. Definirla de nuevo con WIFIAREA.";
        if (id.Value.IsNull || id.Value.IsErased || !id.Value.IsValid)
        {
            return new StoredArea(null, null, $"La polilínea del área de cobertura ya no existe; {fallback}");
        }

        if (tr.GetObject(id.Value, OpenMode.ForRead) is not Polyline polyline)
        {
            return new StoredArea(null, null, $"El área de cobertura guardada no es una polilínea; {fallback}");
        }

        string handle = polyline.Handle.ToString();
        return TryPolygon(polyline, unit, out Polygon2? polygon, out string? error)
            ? new StoredArea(polygon, handle, null)
            : new StoredArea(null, handle, $"Polilínea {handle}: {error}; {fallback}");
    }

    /// <summary>Convierte una polilínea cerrada de tramos rectos en un polígono en metros.</summary>
    public static bool TryPolygon(Polyline polyline, DrawingUnit unit, out Polygon2? polygon, out string? error)
    {
        polygon = null;
        int count = polyline.NumberOfVertices;
        var points = new List<Vec2>(count);
        for (int i = 0; i < count; i++)
        {
            bool closingSegment = i == count - 1;
            if ((!closingSegment || polyline.Closed) && polyline.GetBulgeAt(i) != 0)
            {
                error = "tiene arcos (solo se admiten tramos rectos)";
                return false;
            }

            var p = polyline.GetPoint2dAt(i);
            points.Add(DrawingUnits.ToMetres(unit, p.X, p.Y));
        }

        if (!polyline.Closed && (count < 2 || Vec2.Distance(points[0], points[^1]) > 1e-6))
        {
            error = "no está cerrada";
            return false;
        }

        try
        {
            polygon = new Polygon2(points);
            error = null;
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message.Split(" (")[0].TrimEnd('.').ToLowerInvariant();
            return false;
        }
    }
}
