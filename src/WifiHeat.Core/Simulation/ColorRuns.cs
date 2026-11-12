namespace WifiHeat.Core.Simulation;

/// <summary>Tramo horizontal de celdas consecutivas de una fila con el mismo nivel de la escala.</summary>
public readonly record struct ColorRun(int Row, int StartColumn, int Length, int LevelIndex);

public static class ColorRuns
{
    /// <summary>Agrupa cada fila de la grilla en tramos del mismo nivel, de izquierda a derecha.</summary>
    public static IReadOnlyList<ColorRun> FromGrid(RssiGrid grid)
    {
        GridSpec spec = grid.Spec;
        var runs = new List<ColorRun>();
        for (int row = 0; row < spec.Rows; row++)
        {
            int start = 0;
            int level = SignalScale.IndexOf(grid[0, row]);
            for (int column = 1; column < spec.Columns; column++)
            {
                int current = SignalScale.IndexOf(grid[column, row]);
                if (current == level) continue;

                runs.Add(new ColorRun(row, start, column - start, level));
                start = column;
                level = current;
            }

            runs.Add(new ColorRun(row, start, spec.Columns - start, level));
        }

        return runs;
    }
}
