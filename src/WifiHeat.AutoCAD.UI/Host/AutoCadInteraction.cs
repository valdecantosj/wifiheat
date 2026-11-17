using System.IO;
using System.Globalization;
using System.Windows.Threading;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using WifiHeat.AutoCAD.Reading;
using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.Core.Geometry;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD.UI.Host;

/// <summary>
/// Preguntas del panel al editor de un documento. El panel vive en el contexto de aplicación: las preguntas se hacen
/// dentro de un contexto de comando y todo acceso a la base bloquea el documento. Usar solo desde el hilo de AutoCAD.
/// </summary>
internal sealed class AutoCadInteraction(Document document) : IDrawingInteraction
{
    private const double ZoomMargin = 1.6;

    public string? BusyReason
    {
        get
        {
            if (document.IsDisposed) return "El dibujo de este panel ya no está abierto.";
            if (CoreApp.DocumentManager.MdiActiveDocument != document) return "El panel corresponde a otro dibujo. Activar ese dibujo para usarlo.";

            string command = document.CommandInProgress;
            return string.IsNullOrEmpty(command)
                ? null
                : $"AutoCAD está ejecutando el comando {command}. Terminarlo o cancelarlo con Esc antes de usar el panel.";
        }
    }

    public Task<Vec2?> PickPointAsync(string prompt) => InCommandAsync<Vec2?>(ed =>
    {
        PromptPointResult answer = ed.GetPoint(new PromptPointOptions($"\n{prompt}: ") { AllowNone = true });
        return answer.Status == PromptStatus.OK ? ToMetres(ed, answer.Value) : null;
    });

    public Task<BoundingBox?> PickWindowAsync(string prompt) => InCommandAsync<BoundingBox?>(ed =>
    {
        PromptPointResult first = ed.GetPoint(new PromptPointOptions($"\n{prompt}. Primera esquina: "));
        if (first.Status != PromptStatus.OK) return null;
        PromptPointResult second = ed.GetCorner(new PromptCornerOptions("\nEsquina opuesta: ", first.Value));
        if (second.Status != PromptStatus.OK) return null;

        Vec2 a = ToMetres(ed, first.Value), b = ToMetres(ed, second.Value);
        return new BoundingBox(new Vec2(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y)), new Vec2(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y)));
    });

    public Task<IReadOnlyList<string>?> PickEntitiesAsync(string prompt, bool includeHatches = false)
    {
        PromptSelectionResult current = document.Editor.SelectImplied();
        if (current.Status == PromptStatus.OK)
        {
            string[] preselected = Handles(current.Value.GetObjectIds(), includeHatches);
            if (preselected.Length > 0) return Task.FromResult<IReadOnlyList<string>?>(preselected);
        }

        return InCommandAsync<IReadOnlyList<string>?>(ed =>
        {
            var options = new PromptSelectionOptions { MessageForAdding = $"\n{prompt}" };
            var filter = new SelectionFilter([new TypedValue((int)DxfCode.Start, includeHatches ? "LINE,LWPOLYLINE,HATCH" : "LINE,LWPOLYLINE")]);
            PromptSelectionResult answer = ed.GetSelection(options, filter);
            return answer.Status == PromptStatus.OK ? Handles(answer.Value.GetObjectIds(), includeHatches) : null;
        });
    }

    public Task<string?> PickPolylineAsync(string prompt) => InCommandAsync(ed =>
    {
        var options = new PromptEntityOptions($"\n{prompt}: ");
        options.SetRejectMessage("\nDebe ser una polilínea.");
        options.AddAllowedClass(typeof(Polyline), true);
        PromptEntityResult answer = ed.GetEntity(options);
        return answer.Status == PromptStatus.OK ? answer.ObjectId.Handle.ToString() : null;
    });

    /// <summary>Dos ^C, como Esc en la línea de comandos: la pregunta pendiente termina con Cancel.</summary>
    public void CancelPrompt()
    {
        if (document.IsDisposed) return;

        Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
        document.SendStringToExecute("", true, false, false);
    }

    public void Show(IReadOnlyCollection<string> handles, bool zoom)
    {
        using DocumentLock _ = document.LockDocument();
        Database db = document.Database;
        var ids = new List<ObjectId>();
        Extents3d? extents = null;
        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            foreach (string handle in handles)
            {
                if (!long.TryParse(handle, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out long value)) continue;
                if (!db.TryGetObjectId(new Handle(value), out ObjectId id) || id.IsErased) continue;
                if (tr.GetObject(id, OpenMode.ForRead) is not Entity entity) continue;

                ids.Add(id);
                try
                {
                    Extents3d own = entity.GeometricExtents;
                    if (extents is { } total)
                    {
                        total.AddExtents(own);
                        extents = total;
                    }
                    else
                    {
                        extents = own;
                    }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception)
                {
                }
            }

            tr.Commit();
        }

        if (ids.Count == 0) return;

        Editor ed = document.Editor;
        if (zoom && extents is { } box) ZoomTo(ed, box);
        ed.SetImpliedSelection([.. ids]);
        ed.UpdateScreen();
    }

    public string? AskSavePath(string suggestedPath)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "Exportar mapa de calor",
            Filter = "Imagen PNG (*.png)|*.png",
            DefaultExt = ".png",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = Path.GetFileName(suggestedPath),
        };
        string? folder = Path.GetDirectoryName(suggestedPath);
        if (folder is not null && Directory.Exists(folder)) dialog.InitialDirectory = folder;
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public async Task YieldAsync() => await Dispatcher.Yield(DispatcherPriority.Background);

    /// <summary>
    /// Ejecuta una pregunta del editor en contexto de comando. Las excepciones se capturan dentro del delegado
    /// (si escapan pueden cerrar AutoCAD) y se vuelven a lanzar al volver al panel.
    /// </summary>
    private async Task<T> InCommandAsync<T>(Func<Editor, T> ask)
    {
        T result = default!;
        System.Exception? failure = null;
        Autodesk.AutoCAD.Internal.Utils.SetFocusToDwgView();
        await CoreApp.DocumentManager.ExecuteInCommandContextAsync(
            _ =>
            {
                try
                {
                    result = ask(document.Editor);
                }
                catch (System.Exception ex)
                {
                    failure = ex;
                }

                return Task.CompletedTask;
            },
            null);

        if (failure is not null) throw new InvalidOperationException(failure.Message, failure);
        return result;
    }

    /// <summary>Punto del SCP actual a metros en coordenadas universales.</summary>
    private Vec2 ToMetres(Editor ed, Point3d ucsPoint)
    {
        DrawingUnit unit = DrawingStore.ReadUnit(document.Database) ?? DrawingUnit.Metres;
        Point3d world = ucsPoint.TransformBy(ed.CurrentUserCoordinateSystem);
        return DrawingUnits.ToMetres(unit, world.X, world.Y);
    }

    private static string[] Handles(ObjectId[] ids, bool includeHatches) =>
        [.. ids.Where(id => id.ObjectClass.DxfName is "LINE" or "LWPOLYLINE" || (includeHatches && id.ObjectClass.DxfName == "HATCH"))
            .Select(id => id.Handle.ToString())];

    /// <summary>Encuadra la vista actual en la envolvente dada (coordenadas universales), con margen.</summary>
    private static void ZoomTo(Editor ed, Extents3d extents)
    {
        using ViewTableRecord view = ed.GetCurrentView();
        Matrix3d worldToView =
            (Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target) *
             Matrix3d.Displacement(view.Target - Point3d.Origin) *
             Matrix3d.PlaneToWorld(view.ViewDirection)).Inverse();
        extents.TransformBy(worldToView);

        double ratio = view.Width / view.Height;
        double width = extents.MaxPoint.X - extents.MinPoint.X;
        double height = Math.Max(extents.MaxPoint.Y - extents.MinPoint.Y, width / ratio);
        if (height <= 0) return;

        view.Height = height * ZoomMargin;
        view.Width = view.Height * ratio;
        view.CenterPoint = new Point2d((extents.MinPoint.X + extents.MaxPoint.X) / 2, (extents.MinPoint.Y + extents.MaxPoint.Y) / 2);
        ed.SetCurrentView(view);
    }
}
