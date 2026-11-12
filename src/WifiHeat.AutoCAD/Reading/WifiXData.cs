using Autodesk.AutoCAD.DatabaseServices;
using WifiHeat.Core.Materials;

namespace WifiHeat.AutoCAD.Reading;

/// <summary>
/// XData de la aplicación WIFIHEAT.
/// Muro: (1000 "WALL") (1000 material) (1040 espesor en metros) y, si es un eje generado, (1070 1).
/// AP: (1000 "AP") (1000 nombre) (1070 banda) (1040 potencia TX en dBm).
/// </summary>
public static class WifiXData
{
    private const short GeneratedAxisMark = 1;

    /// <summary>Con generatedAxis null se conserva la marca que ya tenga la entidad.</summary>
    public static void WriteWall(Entity entity, string materialId, double thicknessM, bool? generatedAxis = null)
    {
        bool generated = generatedAxis ?? IsGeneratedAxis(entity);
        using var data = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, WifiHeatNames.AppName),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, WifiHeatNames.WallTag),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, materialId),
            new TypedValue((int)DxfCode.ExtendedDataReal, thicknessM));
        if (generated) data.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, GeneratedAxisMark));
        entity.XData = data;
    }

    /// <summary>True si el muro es un eje creado por la generación automática.</summary>
    public static bool IsGeneratedAxis(Entity entity)
    {
        TypedValue[] values = Read(entity);
        return values.Length > 4 && values[1].Value as string == WifiHeatNames.WallTag && values[4].Value is short mark && mark == GeneratedAxisMark;
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
