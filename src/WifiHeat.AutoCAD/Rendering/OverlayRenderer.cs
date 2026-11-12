using System.Text.RegularExpressions;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using WifiHeat.AutoCAD.Export;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;

namespace WifiHeat.AutoCAD.Rendering;

/// <summary>Entities = 0 si la imagen no se pudo escribir. Warnings: problemas que no impidieron dibujar.</summary>
public sealed record OverlayResult(int Entities, string? ImagePath, IReadOnlyList<string> Warnings);

/// <summary>
/// Mapa de calor como imagen ráster: un PNG opaco dentro del área y transparente fuera, referenciado desde el dibujo,
/// atenuado con la propiedad Fade de la imagen y al fondo del orden de dibujo.
/// Cada cálculo escribe un archivo nuevo (&lt;base&gt;_wifiheat_NNN.png) y borra los anteriores si no están bloqueados.
/// </summary>
public static class RasterOverlayRenderer
{
    public const string ImageDefinitionKey = "WIFIHEAT_OVERLAY";
    private const short NeutralBrightness = 50;
    private const short NeutralContrast = 50;

    public static OverlayResult Render(Database db, Transaction tr, DrawingUnit unit, HeatmapImage image, string folder, string baseName, int fadePercent)
    {
        var warnings = new List<string>();
        string path = NextPath(folder, baseName);
        try
        {
            HeatmapBitmap.Save(image, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.ExternalException)
        {
            warnings.Add($"No se pudo escribir la imagen del mapa en {path} ({ex.Message}). El dibujo conserva el mapa anterior.");
            return new OverlayResult(0, null, warnings);
        }

        ModelSpace.EraseLayer(db, tr, WifiHeatNames.OverlayLayer);
        ObjectId dictionaryId = RasterImageDef.GetImageDictionary(db);
        if (dictionaryId.IsNull) dictionaryId = RasterImageDef.CreateImageDictionary(db);
        var dictionary = (DBDictionary)tr.GetObject(dictionaryId, OpenMode.ForWrite);
        RemoveDefinition(tr, dictionary);

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

        try
        {
            raster.Brightness = (byte)NeutralBrightness;
            raster.Contrast = (byte)NeutralContrast;
            raster.Fade = (byte)Math.Clamp(fadePercent, OverlayFade.MinPercent, OverlayFade.MaxPercent);
        }
        catch (Exception ex)
        {
            warnings.Add($"No se pudo aplicar la atenuación a la imagen ({ex.Message}); se ve con colores plenos.");
        }

        try
        {
            var drawOrder = (DrawOrderTable)tr.GetObject(modelSpace.DrawOrderTableId, OpenMode.ForWrite);
            drawOrder.MoveToBottom(new ObjectIdCollection { rasterId });
        }
        catch (Exception ex)
        {
            warnings.Add($"No se pudo enviar la imagen al fondo del orden de dibujo ({ex.Message}). Usar ORDENAR (DRAWORDER) > Detrás de todo.");
        }

        int locked = DeleteOlderImages(folder, baseName, path);
        if (locked > 0) warnings.Add($"{locked} imagen(es) anteriores del mapa están en uso y no se borraron; se borrarán en un cálculo posterior.");
        return new OverlayResult(1, path, warnings);
    }

    /// <summary>Cambia la atenuación de la imagen ya dibujada. Devuelve cuántas imágenes se modificaron.</summary>
    public static int SetFade(Database db, Transaction tr, int fadePercent)
    {
        int changed = 0;
        foreach (ObjectId id in ModelSpace.Open(db, tr, OpenMode.ForRead))
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not RasterImage raster) continue;
            if (!string.Equals(raster.Layer, WifiHeatNames.OverlayLayer, StringComparison.OrdinalIgnoreCase)) continue;

            raster.UpgradeOpen();
            raster.Fade = (byte)Math.Clamp(fadePercent, OverlayFade.MinPercent, OverlayFade.MaxPercent);
            changed++;
        }

        return changed;
    }

    /// <summary>Siguiente número libre: uno más que el mayor existente.</summary>
    public static string NextPath(string folder, string baseName)
    {
        int next = 1;
        if (Directory.Exists(folder))
        {
            foreach (string file in Directory.EnumerateFiles(folder, $"{baseName}_wifiheat_*.png"))
            {
                Match m = Numbered(baseName).Match(Path.GetFileName(file));
                if (m.Success && int.TryParse(m.Groups[1].Value, out int n)) next = Math.Max(next, n + 1);
            }
        }

        return Path.Combine(folder, $"{baseName}_wifiheat_{next:000}.png");
    }

    private static Regex Numbered(string baseName) =>
        new($"^{Regex.Escape(baseName)}_wifiheat(?:_(\\d+))?\\.png$", RegexOptions.IgnoreCase);

    /// <summary>Borra las imágenes de cálculos anteriores de este dibujo. Devuelve cuántas no se pudieron borrar.</summary>
    private static int DeleteOlderImages(string folder, string baseName, string keep)
    {
        int locked = 0;
        Regex pattern = Numbered(baseName);
        foreach (string file in Directory.EnumerateFiles(folder, $"{baseName}_wifiheat*.png"))
        {
            if (!pattern.IsMatch(Path.GetFileName(file))) continue;
            if (string.Equals(Path.GetFullPath(file), Path.GetFullPath(keep), StringComparison.OrdinalIgnoreCase)) continue;
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                locked++;
            }
        }

        return locked;
    }

    /// <summary>Descarga y borra la definición anterior para que el diccionario de imágenes no acumule entradas.</summary>
    private static void RemoveDefinition(Transaction tr, DBDictionary dictionary)
    {
        if (!dictionary.Contains(ImageDefinitionKey)) return;

        var old = (RasterImageDef)tr.GetObject(dictionary.GetAt(ImageDefinitionKey), OpenMode.ForWrite);
        if (old.IsLoaded) old.Unload(true);
        dictionary.Remove(ImageDefinitionKey);
        old.Erase();
    }
}
