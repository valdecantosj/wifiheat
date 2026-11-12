using WifiHeat.Core.Service;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.UI.ViewModels;

public enum StatusKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>
/// Raíz del panel de un documento. No calcula: muestra y edita datos y llama a <see cref="IWifiHeatService"/>.
/// Usar solo desde el hilo de la interfaz.
/// </summary>
public sealed class PanelViewModel : ObservableObject
{
    private string _statusMessage = "";
    private StatusKind _statusKind = StatusKind.Info;
    private bool _isBusy;
    private string _busyText = "";
    private bool _isInteracting;
    private string _interactionHint = "";

    public PanelViewModel(IWifiHeatService service, IDrawingInteraction interaction, string documentName = "")
    {
        Service = service;
        Interaction = interaction;
        DocumentName = documentName;
        Materials = [.. service.Materials.OrderBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase).Select(m => new MaterialOption(m))];

        Prepare = new PrepareViewModel(this);
        Validation = new ValidationViewModel(this);
        Walls = new WallsViewModel(this);
        AccessPoint = new AccessPointViewModel(this);
        Calculation = new CalculationViewModel(this);
        Query = new QueryViewModel(this);
        Export = new ExportViewModel(this);
        RefreshCommand = new RelayCommand(Refresh);
    }

    public string DocumentName { get; }

    public IReadOnlyList<MaterialOption> Materials { get; }

    public PrepareViewModel Prepare { get; }

    public ValidationViewModel Validation { get; }

    public WallsViewModel Walls { get; }

    public AccessPointViewModel AccessPoint { get; }

    public CalculationViewModel Calculation { get; }

    public QueryViewModel Query { get; }

    public ExportViewModel Export { get; }

    public RelayCommand RefreshCommand { get; }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetField(ref _statusMessage, value);
    }

    public StatusKind StatusKind
    {
        get => _statusKind;
        private set => SetField(ref _statusKind, value);
    }

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

    /// <summary>True mientras el panel espera un punto o una selección en el dibujo.</summary>
    public bool IsInteracting
    {
        get => _isInteracting;
        private set => SetField(ref _isInteracting, value);
    }

    public string InteractionHint
    {
        get => _interactionHint;
        private set => SetField(ref _interactionHint, value);
    }

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
            Report(StatusKind.Error, Unexpected(ex));
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

        Report(StatusKind.Warning, "Primero elegir la unidad del dibujo en «Preparar plano».");
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
            Report(StatusKind.Error, Unexpected(ex));
            return false;
        }
    }

    /// <summary>Acción que espera al usuario en el dibujo; hint se muestra mientras dura.</summary>
    internal async Task RunAsync(string hint, Func<Task> action)
    {
        if (!CanStart()) return;

        InteractionHint = hint;
        IsInteracting = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Report(StatusKind.Error, Unexpected(ex));
        }
        finally
        {
            IsInteracting = false;
            InteractionHint = "";
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
            Report(StatusKind.Error, Unexpected(ex));
        }
        finally
        {
            IsBusy = false;
            BusyText = "";
        }
    }

    internal void Report(StatusKind kind, string message)
    {
        StatusKind = kind;
        StatusMessage = message;
    }

    /// <summary>Muestra errores o, si salió bien, el mensaje de éxito con los avisos. Devuelve si la operación salió bien.</summary>
    internal bool Report<T>(OperationResult<T> result, string success)
    {
        if (!result.Succeeded)
        {
            Report(StatusKind.Error, string.Join(" ", result.Errors));
            return false;
        }

        if (result.Warnings.Count > 0) Report(StatusKind.Warning, $"{success} Aviso: {string.Join(" ", result.Warnings)}");
        else Report(StatusKind.Success, success);
        return true;
    }

    private bool CanStart()
    {
        if (IsInteracting || IsBusy)
        {
            Report(StatusKind.Warning, "Hay una operación del panel en curso en el dibujo. Terminarla o cancelarla con Esc.");
            return false;
        }

        if (Interaction.BusyReason is { } reason)
        {
            Report(StatusKind.Warning, reason);
            return false;
        }

        return true;
    }

    private static string Unexpected(Exception ex) => $"No se pudo completar la operación ({ex.GetType().Name}: {ex.Message}).";
}
