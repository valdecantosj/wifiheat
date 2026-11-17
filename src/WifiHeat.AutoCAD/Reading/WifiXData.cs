using Autodesk.AutoCAD.DatabaseServices;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Simulation;

namespace WifiHeat.AutoCAD.Reading;

/// <summary>Datos de un muro o de una columna guardados en la entidad.</summary>
public sealed record StoredObstacle(ObstacleKind Kind, string? MaterialId, double? ThicknessM, Reinforcement? Reinforcement);

/// <summary>
/// XData de la aplicación WIFIHEAT.
/// Muro: (1000 "WALL") (1000 material) (1040 espesor en metros) y, si es un eje generado, (1070 1).
/// Columna: (1000 "COLUMN") (1000 material) (1070 nivel de armadura) y, si se generó desde una figura del plano, (1070 1).
/// AP: (1000 "AP") (1000 nombre) (1070 banda) (1040 potencia TX en dBm).
/// </summary>
public static class WifiXData
{
    private const short GeneratedMark = 1;

    /// <summary>Con generatedAxis null se conserva la marca que ya tenga la entidad.</summary>
    public static void WriteWall(Entity entity, string materialId, double thicknessM, bool? generatedAxis = null)
    {
        bool generated = generatedAxis ?? IsGeneratedAxis(entity);
        using var data = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, WifiHeatNames.AppName),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, WifiHeatNames.WallTag),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, materialId),
            new TypedValue((int)DxfCode.ExtendedDataReal, thicknessM));
        if (generated) data.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, GeneratedMark));
        entity.XData = data;
    }

    /// <summary>Con generated null se conserva la marca que ya tenga la columna.</summary>
    public static void WriteColumn(Entity entity, string materialId, Reinforcement reinforcement, bool? generated = null)
    {
        bool mark = generated ?? IsGeneratedColumn(entity);
        using var data = new ResultBuffer(
            new TypedValue((int)DxfCode.ExtendedDataRegAppName, WifiHeatNames.AppName),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, WifiHeatNames.ColumnTag),
            new TypedValue((int)DxfCode.ExtendedDataAsciiString, materialId),
            new TypedValue((int)DxfCode.ExtendedDataInteger16, (short)reinforcement));
        if (mark) data.Add(new TypedValue((int)DxfCode.ExtendedDataInteger16, GeneratedMark));
        entity.XData = data;
    }

    /// <summary>Quita los datos de la aplicación: la entidad deja de ser muro, columna o AP.</summary>
    public static void Clear(Entity entity)
    {
        using var data = new ResultBuffer(new TypedValue((int)DxfCode.ExtendedDataRegAppName, WifiHeatNames.AppName));
        entity.XData = data;
    }

    /// <summary>True si el muro es un eje creado por la generación automática.</summary>
    public static bool IsGeneratedAxis(Entity entity)
    {
        TypedValue[] values = Read(entity);
        return values.Length > 4 && values[1].Value as string == WifiHeatNames.WallTag && values[4].Value is short mark && mark == GeneratedMark;
    }

    public static bool IsColumn(Entity entity)
    {
        TypedValue[] values = Read(entity);
        return values.Length > 1 && values[1].Value as string == WifiHeatNames.ColumnTag;
    }

    public static bool IsGeneratedColumn(Entity entity)
    {
        TypedValue[] values = Read(entity);
        return values.Length > 4 && values[1].Value as string == WifiHeatNames.ColumnTag && values[4].Value is short mark && mark == GeneratedMark;
    }

    /// <summary>Datos del obstáculo. Sin datos de la aplicación es un muro sin material ni espesor.</summary>
    public static StoredObstacle ReadObstacle(Entity entity)
    {
        TypedValue[] values = Read(entity);
        string? tag = values.Length > 1 ? values[1].Value as string : null;
        string? material = values.Length > 2 ? values[2].Value as string : null;
        if (string.IsNullOrWhiteSpace(material)) material = null;

        if (tag == WifiHeatNames.ColumnTag)
        {
            Reinforcement? level = values.Length > 3 && values[3].Value is short code && Enum.IsDefined(typeof(Reinforcement), (int)code)
                ? (Reinforcement)code
                : null;
            return new StoredObstacle(ObstacleKind.Column, material, null, level);
        }

        if (tag != WifiHeatNames.WallTag) return new StoredObstacle(ObstacleKind.Wall, null, null, null);

        double? thickness = values.Length > 3 && values[3].Value is double t ? t : null;
        return new StoredObstacle(ObstacleKind.Wall, material, thickness, null);
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
