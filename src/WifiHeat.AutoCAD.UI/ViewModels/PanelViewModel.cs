using WifiHeat.Core.Service;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.UI.ViewModels;

/// <summary>
/// Raíz del panel de un documento. No calcula: muestra y edita datos y llama a <see cref="IWifiHeatService"/>.
/// Usar solo desde el hilo de la interfaz.
/// </summary>
public sealed class PanelViewModel : ObservableObject
{
    private bool _isBusy;
    private string _busyText = "";
    private PanelMode? _activeMode;

    public PanelViewModel(IWifiHeatService service, IDrawingInteraction interaction, string documentName = "", MessageCenter? messages = null)
    {
        Service = service;
        Interaction = interaction;
        DocumentName = documentName;
        Messages = messages ?? new MessageCenter();
        Materials = [.. service.Materials.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).Select(m => new MaterialOption(m))];

        Prepare = new PrepareViewModel(this);
        Validation = new ValidationViewModel(this);
        Walls = new WallsViewModel(this);
        AccessPoint = new AccessPointViewModel(this);
        Calculation = new CalculationViewModel(this);
        Query = new QueryViewModel(this);
        Export = new ExportViewModel(this);
        RefreshCommand = new RelayCommand(Refresh);
        CancelModeCommand = new RelayCommand(CancelMode);
    }

    public string DocumentName { get; }

    public IReadOnlyList<MaterialOption> Materials { get; }

    /// <summary>Mensaje del pie e historial.</summary>
    public MessageCenter Messages { get; }

    public PrepareViewModel Prepare { get; }

    public ValidationViewModel Validation { get; }

    public WallsViewModel Walls { get; }

    public AccessPointViewModel AccessPoint { get; }

    public CalculationViewModel Calculation { get; }

    public QueryViewModel Query { get; }

    public ExportViewModel Export { get; }

    public RelayCommand RefreshCommand { get; }

    /// <summary>Cancela la pregunta pendiente en el dibujo, como Esc.</summary>
    public RelayCommand CancelModeCommand { get; }

    /// <summary>True mientras corre una operación larga (cálculo).</summary>
    public bool IsBusy
    {
        get => _isBusy;
        private set => SetField(ref _isBusy, value);
    }

    public string BusyText
    {
        get => _busyText;
        private set => SetField(ref _busyText, value);
    }

    /// <summary>Modo activo mientras el panel espera un punto o una selección en el dibujo; null si no hay ninguno.</summary>
    public PanelMode? ActiveMode
    {
        get => _activeMode;
        private set
        {
            if (!SetField(ref _activeMode, value)) return;
            OnPropertyChanged(nameof(IsInteracting));
        }
    }

    public bool IsInteracting => ActiveMode is not null;

    internal IWifiHeatService Service { get; }

    internal IDrawingInteraction Interaction { get; }

    /// <summary>Vuelve a leer el dibujo. Lo llama el anfitrión al activar el documento o al terminar un comando.</summary>
    public void Refresh()
    {
        if (IsInteracting || IsBusy) return;
        if (Interaction.BusyReason is not null) return;

        try
        {
            Reload();
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
    }

    internal void Reload()
    {
        Prepare.Update(Service.GetDrawingSetup());
        ValidationSummary summary = Service.Validate();
        Walls.Update(Service.LoadWalls());
        Validation.Update(summary);
        AccessPoint.Update();
        Calculation.Update();
        Query.Requery();
    }

    /// <summary>Las acciones que piden o usan coordenadas del dibujo necesitan la unidad.</summary>
    internal bool RequireUnit()
    {
        if (Prepare.HasUnit) return true;

        Report(StatusKind.Warning, "Falta la unidad del dibujo", "Elegirla primero en «1 · Preparar plano».");
        return false;
    }

    /// <summary>Ejecuta una acción del usuario. Devuelve false si no se pudo empezar o si falló de forma inesperada.</summary>
    internal bool Run(Action action)
    {
        if (!CanStart()) return false;

        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
            return false;
        }
    }

    /// <summary>Acción que espera al usuario en el dibujo. El modo se quita siempre al terminar, cancelar o fallar.</summary>
    internal async Task RunAsync(PanelMode mode, Func<Task> action)
    {
        if (!CanStart()) return;

        ActiveMode = mode;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
        finally
        {
            ActiveMode = null;
        }
    }

    /// <summary>Operación larga en el hilo de la interfaz: muestra el estado de ocupado antes de empezar.</summary>
    internal async Task RunBusyAsync(string text, Action action)
    {
        if (!CanStart()) return;

        BusyText = text;
        IsBusy = true;
        try
        {
            await Interaction.YieldAsync();
            action();
        }
        catch (Exception ex)
        {
            ReportUnexpected(ex);
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    internal void Report(StatusKind kind, string title, string detail = "") => Messages.Post(kind, title, detail);

    /// <summary>
    /// Error con los motivos o, si salió bien, éxito; con avisos queda como advertencia con el mismo título.
    /// Devuelve si la operación salió bien.
    /// </summary>
    internal bool Report<T>(OperationResult<T> result, string failure, string success, string detail = "")
    {
        if (!result.Succeeded)
        {
            Report(StatusKind.Error, failure, string.Join(" ", result.Errors));
            return false;
        }

        if (result.Warnings.Count > 0) Report(StatusKind.Warning, success, string.Join(" ", result.Warnings.Prepend(detail).Where(t => t.Length > 0)));
        else Report(StatusKind.Success, success, detail);
        return true;
    }

    private void CancelMode()
    {
        if (IsInteracting) Interaction.CancelPrompt();
    }

    private bool CanStart()
    {
        if (IsInteracting || IsBusy)
        {
            Report(StatusKind.Warning, "Hay una operación en curso", IsBusy ? "Esperar a que termine." : "Terminarla en el dibujo o cancelarla con Esc.");
            return false;
        }

        if (Interaction.BusyReason is { } reason)
        {
            Report(StatusKind.Warning, "El dibujo no está disponible", reason);
            return false;
        }

        Messages.BeginAction();
        return true;
    }

    private void ReportUnexpected(Exception ex) =>
        Report(StatusKind.Error, "No se pudo completar la operación", $"{ex.Message} ({ex.GetType().Name}).");
}
