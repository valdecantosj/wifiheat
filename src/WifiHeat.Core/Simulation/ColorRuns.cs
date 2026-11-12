namespace WifiHeat.Core.Simulation;

/// <summary>Tramo horizontal de celdas consecutivas de una fila con el mismo nivel de la escala.</summary>
public readonly record struct ColorRun(int Row, int StartColumn, int Length, int LevelIndex);

public static class ColorRuns
{
    /// <summary>Agrupa cada fila de la grilla en tramos del mismo nivel, de izquierda a derecha. Las celdas sin valor no forman tramos.</summary>
    public static IReadOnlyList<ColorRun> FromGrid(RssiGrid grid)
    {
        GridSpec spec = grid.Spec;
        var runs = new List<ColorRun>();
        for (int row = 0; row < spec.Rows; row++)
        {
            int start = 0;
            int level = -1;
            for (int column = 0; column <= spec.Columns; column++)
            {
                int current = column < spec.Columns && grid.HasValue(column, row) ? SignalScale.IndexOf(grid[column, row]) : -1;
                if (current == level) continue;

                if (level >= 0) runs.Add(new ColorRun(row, start, column - start, level));
                start = column;
                level = current;
            }
        }

        return runs;
    }
}
