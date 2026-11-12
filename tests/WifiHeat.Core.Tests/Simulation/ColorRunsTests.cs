using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;
using Xunit;

namespace WifiHeat.Core.Tests.Simulation;

public class ColorRunsTests
{
    private static RssiGrid Grid(double[,] valuesByColumnRow) =>
        new(new GridSpec(new Vec2(0, 0), 0.5, valuesByColumnRow.GetLength(0), valuesByColumnRow.GetLength(1)), valuesByColumnRow);

    [Fact]
    public void Uniform_row_is_a_single_run()
    {
        var values = new double[4, 1];
        for (int c = 0; c < 4; c++) values[c, 0] = -40;

        ColorRun run = Assert.Single(ColorRuns.FromGrid(Grid(values)));

        Assert.Equal(new ColorRun(0, 0, 4, 0), run);
    }

    [Fact]
    public void Splits_when_the_level_changes()
    {
        var values = new double[5, 1];
        values[0, 0] = -40; values[1, 0] = -45; values[2, 0] = -70; values[3, 0] = -72; values[4, 0] = -40;

        IReadOnlyList<ColorRun> runs = ColorRuns.FromGrid(Grid(values));

        Assert.Equal(
            [new ColorRun(0, 0, 2, 0), new ColorRun(0, 2, 2, SignalScale.IndexOf(-70)), new ColorRun(0, 4, 1, 0)],
            runs);
    }

    [Fact]
    public void Runs_do_not_span_rows_and_cover_every_cell()
    {
        var values = new double[3, 2];
        for (int c = 0; c < 3; c++) { values[c, 0] = -40; values[c, 1] = -40; }

        IReadOnlyList<ColorRun> runs = ColorRuns.FromGrid(Grid(values));

        Assert.Equal(2, runs.Count);
        Assert.Equal(6, runs.Sum(r => r.Length));
        Assert.Equal([0, 1], runs.Select(r => r.Row));
    }

    [Fact]
    public void Run_level_matches_the_scale_of_each_cell()
    {
        var values = new double[6, 3];
        var random = new Random(7);
        for (int c = 0; c < 6; c++)
            for (int r = 0; r < 3; r++)
                values[c, r] = -30 - random.NextDouble() * 70;
        RssiGrid grid = Grid(values);

        foreach (ColorRun run in ColorRuns.FromGrid(grid))
        {
            for (int c = run.StartColumn; c < run.StartColumn + run.Length; c++)
            {
                Assert.Equal(run.LevelIndex, SignalScale.IndexOf(grid[c, run.Row]));
            }
        }
    }
}
