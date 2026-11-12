using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Windows;
using WifiHeat.AutoCAD.UI.ViewModels;
using WifiHeat.AutoCAD.UI.Views;
using CoreApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace WifiHeat.AutoCAD.UI.Host;

/// <summary>
/// Paleta acoplable con el panel. Hay una sola vista; cada documento tiene su ViewModel sobre el servicio de ese documento.
/// Todo corre en el hilo de AutoCAD: el panel se refresca al activar un documento y cuando termina un comando.
/// </summary>
internal static class PanelHost
{
    private static readonly Guid PaletteId = new("5B0F1C6E-6A1D-4B6C-9A55-3D2C8C1E7F41");
    private static readonly Dictionary<Document, PanelViewModel> Panels = [];
    private static PaletteSet? _palette;
    private static PanelView? _view;
    private static bool _refreshPending;

    public static void Show()
    {
        if (_palette is null) Create();

        _palette!.Visible = true;
        ApplyTheme();
        Activate(CoreApp.DocumentManager.MdiActiveDocument);
    }

    private static void Create()
    {
        _view = new PanelView();
        _palette = new PaletteSet("WifiHeat", "WIFIPANEL", PaletteId)
        {
            Style = PaletteSetStyles.ShowCloseButton | PaletteSetStyles.ShowAutoHideButton | PaletteSetStyles.ShowPropertiesMenu,
            DockEnabled = DockSides.Left | DockSides.Right,
            MinimumSize = new System.Drawing.Size(300, 360),
            KeepFocus = true,
        };
        _palette.AddVisual("WifiHeat", _view, true);
        _palette.Size = new System.Drawing.Size(420, 760);

        DocumentCollection documents = CoreApp.DocumentManager;
        documents.DocumentActivated += (_, e) => Activate(e.Document);
        documents.DocumentToBeDestroyed += (_, e) => Forget(e.Document);
        documents.DocumentDestroyed += (_, _) =>
        {
            if (CoreApp.DocumentManager.Count == 0) Activate(null);
        };
        CoreApp.SystemVariableChanged += (_, e) =>
        {
            if (string.Equals(e.Name, "COLORTHEME", StringComparison.OrdinalIgnoreCase)) ApplyTheme();
        };
    }

    private static void Activate(Document? document)
    {
        if (_view is null) return;

        if (document is null)
        {
            _view.DataContext = null;
            return;
        }

        try
        {
            if (!Panels.TryGetValue(document, out PanelViewModel? panel))
            {
                panel = new PanelViewModel(Sessions.Service(document), new AutoCadInteraction(document), Path.GetFileName(document.Name));
                Panels[document] = panel;
                document.CommandEnded += OnCommandFinished;
                document.CommandCancelled += OnCommandFinished;
                document.CommandFailed += OnCommandFinished;
            }

            _view.DataContext = panel;
            panel.Refresh();
        }
        catch (System.Exception ex)
        {
            _view.DataContext = null;
            document.Editor.WriteMessage($"\nWIFIPANEL: no se pudo preparar el panel para este dibujo ({ex.GetType().Name}: {ex.Message}).\n");
        }
    }

    private static void Forget(Document document)
    {
        if (!Panels.Remove(document, out PanelViewModel? panel)) return;

        document.CommandEnded -= OnCommandFinished;
        document.CommandCancelled -= OnCommandFinished;
        document.CommandFailed -= OnCommandFinished;
        if (_view is not null && ReferenceEquals(_view.DataContext, panel)) _view.DataContext = null;
    }

    /// <summary>Un comando pudo cambiar muros, AP o área: se relee el dibujo cuando AutoCAD queda libre.</summary>
    private static void OnCommandFinished(object? sender, CommandEventArgs e)
    {
        if (_refreshPending || _palette is not { Visible: true }) return;

        _refreshPending = true;
        CoreApp.Idle += OnIdle;
    }

    private static void OnIdle(object? sender, EventArgs e)
    {
        CoreApp.Idle -= OnIdle;
        _refreshPending = false;
        Document? active = CoreApp.DocumentManager.MdiActiveDocument;
        if (active is not null && Panels.TryGetValue(active, out PanelViewModel? panel)) panel.Refresh();
    }

    /// <summary>Sigue el tema de AutoCAD: COLORTHEME 0 = oscuro, 1 = claro.</summary>
    private static void ApplyTheme()
    {
        if (_view is null) return;

        bool dark = true;
        try
        {
            dark = Convert.ToInt32(CoreApp.GetSystemVariable("COLORTHEME")) == 0;
        }
        catch (System.Exception)
        {
        }

        _view.ApplyTheme(dark);
    }
}
