using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace WifiHeat.AutoCAD.Rendering;

public interface IOverlayRenderer
{
    /// <summary>Reemplaza el mapa de calor del dibujo. Devuelve la cantidad de entidades creadas.</summary>
    int Render(Database db, Transaction tr, DrawingUnit unit, RssiGrid grid);
}

/// <summary>Mapa de calor con un sólido 2D por tramo horizontal del mismo color.</summary>
public sealed class SolidRunOverlayRenderer : IOverlayRenderer
{
    public const byte Alpha = 150;

    public int Render(Database db, Transaction tr, DrawingUnit unit, RssiGrid grid)
    {
        ModelSpace.EraseLayer(db, tr, WifiHeatNames.OverlayLayer);
        BlockTableRecord modelSpace = ModelSpace.Open(db, tr, OpenMode.ForWrite);

        GridSpec spec = grid.Spec;
        Color[] colors = [.. SignalScale.Levels.Select(l => Color.FromRgb(l.R, l.G, l.B))];
        var transparency = new Transparency(Alpha);
        var ids = new ObjectIdCollection();

        foreach (ColorRun run in ColorRuns.FromGrid(grid))
        {
            double x0 = spec.Origin.X + run.StartColumn * spec.CellSizeM;
            double x1 = x0 + run.Length * spec.CellSizeM;
            double y0 = spec.Origin.Y + run.Row * spec.CellSizeM;
            double y1 = y0 + spec.CellSizeM;

            var solid = new Solid(
                ModelSpace.ToDrawing(unit, x0, y0),
                ModelSpace.ToDrawing(unit, x1, y0),
                ModelSpace.ToDrawing(unit, x0, y1),
                ModelSpace.ToDrawing(unit, x1, y1))
            {
                Layer = WifiHeatNames.OverlayLayer,
                Color = colors[run.LevelIndex],
                Transparency = transparency,
            };
            ids.Add(ModelSpace.Add(modelSpace, tr, solid));
        }

        if (ids.Count > 0)
        {
            var drawOrder = (DrawOrderTable)tr.GetObject(modelSpace.DrawOrderTableId, OpenMode.ForWrite);
            drawOrder.MoveToBottom(ids);
        }

        return ids.Count;
    }
}
