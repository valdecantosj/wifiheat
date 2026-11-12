using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using WifiHeat.AutoCAD.Reading;
using WifiHeat.Core.Geometry;

namespace WifiHeat.AutoCAD.Commands;

/// <summary>Entrada común de los comandos: unidad guardada y puntos convertidos a metros.</summary>
public static class DrawingContext
{
    public static bool TryGetUnit(Document doc, string command, out DrawingUnit unit)
    {
        DrawingUnit? stored = DrawingStore.ReadUnit(doc.Database);
        unit = stored ?? DrawingUnit.Metres;
        if (stored is null)
        {
            doc.Editor.WriteMessage($"\n{command}: el dibujo no tiene unidad guardada. Ejecutar WIFISETUP.\n");
        }

        return stored is not null;
    }

    /// <summary>Pide un punto y lo devuelve en metros, en coordenadas universales.</summary>
    public static bool TryGetPointM(Editor ed, DrawingUnit unit, string prompt, out Vec2 pointM)
    {
        pointM = default;
        PromptPointResult answer = ed.GetPoint(new PromptPointOptions(prompt));
        if (answer.Status != PromptStatus.OK) return false;

        Point3d world = answer.Value.TransformBy(ed.CurrentUserCoordinateSystem);
        pointM = DrawingUnits.ToMetres(unit, world.X, world.Y);
        return true;
    }
}
