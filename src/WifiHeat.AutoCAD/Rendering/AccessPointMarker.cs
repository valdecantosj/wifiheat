using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using WifiHeat.AutoCAD.Reading;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Propagation;
using WifiHeat.Core.Simulation;

namespace WifiHeat.AutoCAD.Rendering;

/// <summary>Marcador del AP: círculo con XData y texto con el nombre. Reemplaza al anterior.</summary>
public static class AccessPointMarker
{
    public const double RadiusM = 0.15;
    public const double TextHeightM = 0.25;

    public static void Draw(Database db, Transaction tr, DrawingUnit unit, AccessPoint ap, RadioSettings settings)
    {
        DrawingStore.EnsureAll(db, tr);
        ModelSpace.EraseLayer(db, tr, WifiHeatNames.AccessPointLayer);
        BlockTableRecord modelSpace = ModelSpace.Open(db, tr, OpenMode.ForWrite);

        double radius = DrawingUnits.FromMetres(unit, RadiusM);
        var circle = new Circle(ModelSpace.ToDrawing(unit, ap.Position), Vector3d.ZAxis, radius)
        {
            Layer = WifiHeatNames.AccessPointLayer,
        };
        ModelSpace.Add(modelSpace, tr, circle);
        WifiXData.WriteAccessPoint(circle, ap.Name, settings.Band, settings.TxPowerDbm);

        var label = new DBText
        {
            Position = ModelSpace.ToDrawing(unit, ap.Position.X + 1.5 * RadiusM, ap.Position.Y + 1.5 * RadiusM),
            Height = DrawingUnits.FromMetres(unit, TextHeightM),
            TextString = ap.Name,
            Layer = WifiHeatNames.AccessPointLayer,
        };
        ModelSpace.Add(modelSpace, tr, label);
    }
}
