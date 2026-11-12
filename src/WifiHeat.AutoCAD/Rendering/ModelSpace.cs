using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using WifiHeat.Core.Geometry;

namespace WifiHeat.AutoCAD.Rendering;

/// <summary>Operaciones comunes sobre el espacio modelo para las capas propias del plugin.</summary>
public static class ModelSpace
{
    public static BlockTableRecord Open(Database db, Transaction tr, OpenMode mode)
    {
        var blocks = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        return (BlockTableRecord)tr.GetObject(blocks[BlockTableRecord.ModelSpace], mode);
    }

    /// <summary>Borra todas las entidades de una capa. Devuelve la cantidad borrada.</summary>
    public static int EraseLayer(Database db, Transaction tr, string layer)
    {
        int erased = 0;
        foreach (ObjectId id in Open(db, tr, OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;
            if (!string.Equals(entity.Layer, layer, StringComparison.OrdinalIgnoreCase)) continue;

            entity.UpgradeOpen();
            entity.Erase();
            erased++;
        }

        return erased;
    }

    public static ObjectId Add(BlockTableRecord modelSpace, Transaction tr, Entity entity)
    {
        ObjectId id = modelSpace.AppendEntity(entity);
        tr.AddNewlyCreatedDBObject(entity, true);
        return id;
    }

    public static Point3d ToDrawing(DrawingUnit unit, Vec2 metres) =>
        new(DrawingUnits.FromMetres(unit, metres.X), DrawingUnits.FromMetres(unit, metres.Y), 0);

    public static Point3d ToDrawing(DrawingUnit unit, double xM, double yM) => ToDrawing(unit, new Vec2(xM, yM));
}
