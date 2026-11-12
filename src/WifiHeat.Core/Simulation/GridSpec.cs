using WifiHeat.Core.Geometry;

namespace WifiHeat.Core.Simulation;

/// <summary>Grilla regular en metros. Cada celda se calcula en su centro.</summary>
public sealed record GridSpec(Vec2 Origin, double CellSizeM, int Columns, int Rows)
{
    /// <summary>Por encima de este número de celdas se emite una advertencia (área desproporcionada).</summary>
    public const int LargeGridWarningCells = 200_000;

    public int CellCount => Columns * Rows;

    public Vec2 CellCenter(int column, int row) =>
        new(Origin.X + (column + 0.5) * CellSizeM, Origin.Y + (row + 0.5) * CellSizeM);

    public Vec2 CellMin(int column, int row) => new(Origin.X + column * CellSizeM, Origin.Y + row * CellSizeM);

    /// <summary>Grilla que cubre la envolvente completa, alineada a múltiplos de la celda.</summary>
    public static GridSpec Cover(BoundingBox bounds, double cellSizeM)
    {
        if (cellSizeM <= 0) throw new ArgumentOutOfRangeException(nameof(cellSizeM));
        double originX = Math.Floor(bounds.Min.X / cellSizeM) * cellSizeM;
        double originY = Math.Floor(bounds.Min.Y / cellSizeM) * cellSizeM;
        int columns = Math.Max(1, (int)Math.Ceiling((bounds.Max.X - originX) / cellSizeM - 1e-9));
        int rows = Math.Max(1, (int)Math.Ceiling((bounds.Max.Y - originY) / cellSizeM - 1e-9));
        return new GridSpec(new Vec2(originX, originY), cellSizeM, columns, rows);
    }
}
