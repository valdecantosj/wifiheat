using Autodesk.AutoCAD.DatabaseServices;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;
using Color = Autodesk.AutoCAD.Colors.Color;

namespace WifiHeat.AutoCAD.Rendering;

/// <summary>Leyenda a la derecha del mapa: título, parámetros y un rectángulo de color por rango.</summary>
public static class LegendRenderer
{
    public static void Render(Database db, Transaction tr, DrawingUnit unit, GridSpec spec, Legend legend)
    {
        ModelSpace.EraseLayer(db, tr, WifiHeatNames.LegendLayer);
        BlockTableRecord modelSpace = ModelSpace.Open(db, tr, OpenMode.ForWrite);

        double row = Legend.RowHeightM(Math.Max(spec.WidthM, spec.HeightM));
        double left = spec.Origin.X + spec.WidthM + row;
        double top = spec.Origin.Y + spec.HeightM;
        int line = 0;

        AddText(legend.Title, 0.6);
        foreach (string detail in legend.Details) AddText(detail, 0.45);
        foreach (SignalLevel level in legend.Levels)
        {
            double y = top - (line + 1) * row;
            var swatch = new Solid(
                ModelSpace.ToDrawing(unit, left, y),
                ModelSpace.ToDrawing(unit, left + 1.2 * row, y),
                ModelSpace.ToDrawing(unit, left, y + 0.8 * row),
                ModelSpace.ToDrawing(unit, left + 1.2 * row, y + 0.8 * row))
            {
                Layer = WifiHeatNames.LegendLayer,
                Color = Color.FromRgb(level.R, level.G, level.B),
                Transparency = new Autodesk.AutoCAD.Colors.Transparency(HeatmapRasterizer.DefaultAlpha),
            };
            ModelSpace.Add(modelSpace, tr, swatch);
            AddText(level.Label, 0.45, 1.6 * row);
        }

        void AddText(string text, double heightFactor, double offsetM = 0)
        {
            double y = top - (line + 1) * row + 0.15 * row;
            var entity = new DBText
            {
                Position = ModelSpace.ToDrawing(unit, left + offsetM, y),
                Height = DrawingUnits.FromMetres(unit, heightFactor * row),
                TextString = Plain(text),
                Layer = WifiHeatNames.LegendLayer,
            };
            ModelSpace.Add(modelSpace, tr, entity);
            line++;
        }
    }

    /// <summary>Las fuentes SHX no traen estos símbolos.</summary>
    private static string Plain(string text) => text.Replace("−", "-").Replace("≥", ">=");
}
