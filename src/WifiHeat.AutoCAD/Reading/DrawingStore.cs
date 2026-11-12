using Color = Autodesk.AutoCAD.Colors.Color;
using ColorMethod = Autodesk.AutoCAD.Colors.ColorMethod;
using Autodesk.AutoCAD.DatabaseServices;
using WifiHeat.Core.Geometry;

namespace WifiHeat.AutoCAD.Reading;

/// <summary>Capas, aplicación registrada y unidad del dibujo guardada en el diccionario de objetos con nombre.</summary>
public static class DrawingStore
{
    public static void EnsureAll(Database db, Transaction tr)
    {
        EnsureRegApp(db, tr);
        EnsureLayer(db, tr, WifiHeatNames.WallLayer, 4);
        EnsureLayer(db, tr, WifiHeatNames.AccessPointLayer, 5);
        EnsureLayer(db, tr, WifiHeatNames.OverlayLayer, 7);
        EnsureLayer(db, tr, WifiHeatNames.LegendLayer, 7);
    }

    public static void EnsureRegApp(Database db, Transaction tr)
    {
        var table = (RegAppTable)tr.GetObject(db.RegAppTableId, OpenMode.ForRead);
        if (table.Has(WifiHeatNames.AppName)) return;

        table.UpgradeOpen();
        var record = new RegAppTableRecord { Name = WifiHeatNames.AppName };
        table.Add(record);
        tr.AddNewlyCreatedDBObject(record, true);
    }

    public static ObjectId EnsureLayer(Database db, Transaction tr, string name, short colorIndex)
    {
        var table = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
        if (table.Has(name)) return table[name];

        table.UpgradeOpen();
        var record = new LayerTableRecord
        {
            Name = name,
            Color = Color.FromColorIndex(ColorMethod.ByAci, colorIndex),
        };
        ObjectId id = table.Add(record);
        tr.AddNewlyCreatedDBObject(record, true);
        return id;
    }

    public static void WriteUnit(Database db, Transaction tr, DrawingUnit unit)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        using var data = new ResultBuffer(new TypedValue((int)DxfCode.Text, DrawingUnits.Code(unit)));
        if (nod.Contains(WifiHeatNames.UnitRecordKey))
        {
            var existing = (Xrecord)tr.GetObject(nod.GetAt(WifiHeatNames.UnitRecordKey), OpenMode.ForWrite);
            existing.Data = data;
            return;
        }

        var record = new Xrecord { Data = data };
        nod.SetAt(WifiHeatNames.UnitRecordKey, record);
        tr.AddNewlyCreatedDBObject(record, true);
    }

    public static DrawingUnit? ReadUnit(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(WifiHeatNames.UnitRecordKey)) return null;
        if (tr.GetObject(nod.GetAt(WifiHeatNames.UnitRecordKey), OpenMode.ForRead) is not Xrecord record) return null;

        using ResultBuffer? data = record.Data;
        if (data is null) return null;
        foreach (TypedValue value in data)
        {
            if (value.Value is string code && DrawingUnits.TryParseCode(code, out DrawingUnit unit)) return unit;
        }

        return null;
    }

    public static DrawingUnit? ReadUnit(Database db)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        DrawingUnit? unit = ReadUnit(db, tr);
        tr.Commit();
        return unit;
    }

    /// <summary>Guarda en el NOD una referencia a la polilínea del área de cobertura; null la quita.</summary>
    public static void WriteAreaId(Database db, Transaction tr, ObjectId? polylineId)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForWrite);
        if (polylineId is null)
        {
            if (!nod.Contains(WifiHeatNames.AreaRecordKey)) return;
            DBObject old = tr.GetObject(nod.GetAt(WifiHeatNames.AreaRecordKey), OpenMode.ForWrite);
            nod.Remove(WifiHeatNames.AreaRecordKey);
            old.Erase();
            return;
        }

        using var data = new ResultBuffer(new TypedValue((int)DxfCode.SoftPointerId, polylineId.Value));
        if (nod.Contains(WifiHeatNames.AreaRecordKey))
        {
            var existing = (Xrecord)tr.GetObject(nod.GetAt(WifiHeatNames.AreaRecordKey), OpenMode.ForWrite);
            existing.Data = data;
            return;
        }

        var record = new Xrecord { Data = data };
        nod.SetAt(WifiHeatNames.AreaRecordKey, record);
        tr.AddNewlyCreatedDBObject(record, true);
    }

    /// <summary>Devuelve la referencia guardada, aunque la entidad ya no exista; null si no hay área definida.</summary>
    public static ObjectId? ReadAreaId(Database db, Transaction tr)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        if (!nod.Contains(WifiHeatNames.AreaRecordKey)) return null;
        if (tr.GetObject(nod.GetAt(WifiHeatNames.AreaRecordKey), OpenMode.ForRead) is not Xrecord record) return null;

        using ResultBuffer? data = record.Data;
        if (data is null) return ObjectId.Null;
        foreach (TypedValue value in data)
        {
            if (value.Value is ObjectId id) return id;
        }

        return ObjectId.Null;
    }

    public static DrawingUnit SuggestUnit(Database db) => db.Insunits switch
    {
        UnitsValue.Centimeters => DrawingUnit.Centimetres,
        UnitsValue.Millimeters => DrawingUnit.Millimetres,
        _ => DrawingUnit.Metres,
    };
}
