using WifiHeat.Core.Geometry;
using Xunit;

namespace WifiHeat.Core.Tests.Geometry;

public class DrawingUnitsTests
{
    [Theory]
    [InlineData(DrawingUnit.Metres, 3.5, 3.5)]
    [InlineData(DrawingUnit.Centimetres, 350, 3.5)]
    [InlineData(DrawingUnit.Millimetres, 3500, 3.5)]
    public void Converts_to_metres(DrawingUnit unit, double value, double expected)
    {
        Assert.Equal(expected, DrawingUnits.ToMetres(unit, value), 9);
    }

    [Theory]
    [InlineData(DrawingUnit.Metres)]
    [InlineData(DrawingUnit.Centimetres)]
    [InlineData(DrawingUnit.Millimetres)]
    public void Round_trips_metres_and_code(DrawingUnit unit)
    {
        Assert.Equal(12.34, DrawingUnits.ToMetres(unit, DrawingUnits.FromMetres(unit, 12.34)), 9);
        Assert.True(DrawingUnits.TryParseCode(DrawingUnits.Code(unit), out DrawingUnit parsed));
        Assert.Equal(unit, parsed);
    }

    [Fact]
    public void Converts_points()
    {
        Assert.Equal(new Vec2(1, 2), DrawingUnits.ToMetres(DrawingUnit.Millimetres, 1000, 2000));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("in")]
    public void Rejects_unknown_codes(string? code)
    {
        Assert.False(DrawingUnits.TryParseCode(code, out _));
    }
}
