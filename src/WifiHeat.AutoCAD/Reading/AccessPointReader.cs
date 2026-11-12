using Autodesk.AutoCAD.DatabaseServices;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;

namespace WifiHeat.AutoCAD.Reading;

public sealed record StoredAccessPoint(Vec2 Position, Band Band, double TxPowerDbm);

/// <summary>Recupera el AP guardado en el dibujo (círculo con XData en la capa de AP).</summary>
public static class AccessPointReader
{
    public static StoredAccessPoint? Read(Database db)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        StoredAccessPoint? result = Read(db, tr);
        tr.Commit();
        return result;
    }

    private static StoredAccessPoint? Read(Database db, Transaction tr)
    {
        DrawingUnit? unit = DrawingStore.ReadUnit(db, tr);
        if (unit is null) return null;

        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        var modelSpace = (BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace], OpenMode.ForRead);
        foreach (ObjectId id in modelSpace)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Circle circle) continue;
            if (!string.Equals(circle.Layer, WifiHeatNames.AccessPointLayer, StringComparison.OrdinalIgnoreCase)) continue;
            if (!WifiXData.TryReadAccessPoint(circle, out Band band, out double txPowerDbm)) continue;

            return new StoredAccessPoint(DrawingUnits.ToMetres(unit.Value, circle.Center.X, circle.Center.Y), band, txPowerDbm);
        }

        return null;
    }
}
