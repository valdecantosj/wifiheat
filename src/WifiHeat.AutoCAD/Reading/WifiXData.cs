using Autodesk.AutoCAD.DatabaseServices;
using WifiHeat.Core.Materials;

namespace WifiHeat.AutoCAD.Reading;

/// <summary>
/// XData de la aplicación WIFIHEAT.
/// Muro: (1000 "WALL") (1000 material) (1040 espesor en metros).
/// AP: (1000 "AP") (1000 nombre) (1070 banda) (1040 potencia TX en dBm).
/// </summary>
public static class WifiXData
{
    public static void WriteWall(Entity entity, string materialId, double thicknessM)
    {
        using var data = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, WifiHeatNames.AppName),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, WifiHeatNames.WallTag),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, materialId),
            new TypedValue((int)DxfCode.ExtendedDataReal, thicknessM));
        entity.XData = data;
    }

    public static (string? MaterialId, double? ThicknessM) ReadWall(Entity entity)
    {
        TypedValue[] values = Read(entity);
        if (values.Length < 2 || values[1].Value as string != WifiHeatNames.WallTag) return (null, null);

        string? material = values.Length > 2 ? values[2].Value as string : null;
        double? thickness = values.Length > 3 && values[3].Value is double t ? t : null;
        return (string.IsNullOrWhiteSpace(material) ? null : material, thickness);
    }

    public static void WriteAccessPoint(Entity entity, string name, Band band, double txPowerDbm)
    {
        using var data = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, WifiHeatNames.AppName),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, WifiHeatNames.AccessPointTag),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, name),
            new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)band),
            new TypedValue((int)DxfCode.ExtendedDataReal, txPowerDbm));
        entity.XData = data;
    }

    public static bool TryReadAccessPoint(Entity entity, out Band band, out double txPowerDbm)
    {
        band = Band.Ghz2_4;
        txPowerDbm = 0;
        TypedValue[] values = Read(entity);
        if (values.Length < 5 || values[1].Value as string != WifiHeatNames.AccessPointTag) return false;
        if (values[3].Value is not short bandValue || values[4].Value is not double tx) return false;
        if (!Enum.IsDefined(typeof(Band), (int)bandValue)) return false;

        band = (Band)bandValue;
        txPowerDbm = tx;
        return true;
    }

    private static TypedValue[] Read(Entity entity)
    {
        using ResultBuffer? data = entity.GetXDataForApplication(WifiHeatNames.AppName);
        return data?.AsArray() ?? [];
    }
}
