using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using WifiHeat.AutoCAD.Export;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;

namespace WifiHeat.AutoCAD.Rendering;

public sealed record OverlayResult(int Entities, string ImagePath);

public interface IOverlayRenderer
{
    /// <summary>Reemplaza el mapa de calor del dibujo.</summary>
    OverlayResult Render(Database db, Transaction tr, DrawingUnit unit, HeatmapImage image, string imagePath);
}

/// <summary>
/// Mapa de calor como imagen ráster: un PNG con alfa referenciado desde el dibujo,
/// en la capa del overlay y al fondo del orden de dibujo.
/// </summary>
public sealed class RasterOverlayRenderer : IOverlayRenderer
{
    public const string ImageDefinitionKey = "WIFIHEAT_OVERLAY";

    public OverlayResult Render(Database db, Transaction tr, DrawingUnit unit, HeatmapImage image, string imagePath)
    {
        ModelSpace.EraseLayer(db, tr, WifiHeatNames.OverlayLayer);
        ObjectId dictionaryId = RasterImageDef.GetImageDictionary(db);
        if (dictionaryId.IsNull) dictionaryId = RasterImageDef.CreateImageDictionary(db);
        var dictionary = (DBDictionary)tr.GetObject(dictionaryId, OpenMode.ForWrite);
        RemoveDefinition(tr, dictionary);

        string path = WriteImage(image, imagePath);

        var definition = new RasterImageDef { SourceFileName = path };
        definition.Load();
        ObjectId definitionId = dictionary.SetAt(ImageDefinitionKey, definition);
        tr.AddNewlyCreatedDBObject(definition, true);

        BlockTableRecord modelSpace = ModelSpace.Open(db, tr, OpenMode.ForWrite);
        var raster = new RasterImage
        {
            ImageDefId = definitionId,
            Layer = WifiHeatNames.OverlayLayer,
            ShowImage = true,
            ImageTransparency = true,
        };
        raster.Orientation = new CoordinateSystem3d(
            ModelSpace.ToDrawing(unit, image.Origin),
            new Vector3d(DrawingUnits.FromMetres(unit, image.WidthM), 0, 0),
            new Vector3d(0, DrawingUnits.FromMetres(unit, image.HeightM), 0));
        ObjectId rasterId = ModelSpace.Add(modelSpace, tr, raster);
        RasterImage.EnableReactors(true);
        raster.AssociateRasterDef(definition);

        var drawOrder = (DrawOrderTable)tr.GetObject(modelSpace.DrawOrderTableId, OpenMode.ForWrite);
        drawOrder.MoveToBottom(new ObjectIdCollection { rasterId });
        return new OverlayResult(1, path);
    }

    private static void RemoveDefinition(Transaction tr, DBDictionary dictionary)
    {
        if (!dictionary.Contains(ImageDefinitionKey)) return;

        var old = (RasterImageDef)tr.GetObject(dictionary.GetAt(ImageDefinitionKey), OpenMode.ForWrite);
        if (old.IsLoaded) old.Unload(true);
        dictionary.Remove(ImageDefinitionKey);
        old.Erase();
    }

    /// <summary>Si el archivo está bloqueado (por ejemplo, abierto en otro programa), se escribe con otro nombre.</summary>
    private static string WriteImage(HeatmapImage image, string path)
    {
        try
        {
            HeatmapBitmap.Save(image, path);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            string alternative = Path.Combine(Path.GetDirectoryName(path)!,
                $"{Path.GetFileNameWithoutExtension(path)}-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            HeatmapBitmap.Save(image, alternative);
            return alternative;
        }
    }
}
