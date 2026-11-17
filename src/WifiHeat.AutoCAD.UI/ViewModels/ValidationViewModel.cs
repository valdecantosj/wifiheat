using System.Collections.ObjectModel;
using WifiHeat.Core.Service;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.UI.ViewModels;

public sealed class ValidationItemViewModel(ValidationItem item)
{
    public bool IsError { get; } = item.Severity == IssueSeverity.Error;

    public string SeverityLabel { get; } = item.Severity == IssueSeverity.Error ? "Error" : "Aviso";

    public string Code { get; } = item.Code;

    public string? Handle { get; } = item.Handle;

    public bool HasHandle { get; } = item.Handle is not null;

    public string Message { get; } = item.Message;

    public string Title { get; } = item.Handle is null ? item.Code : $"{item.Code} · {item.Handle}";
}

/// <summary>Sección "Plano y validación": problemas del plano con el handle de la entidad a corregir.</summary>
public sealed class ValidationViewModel : ObservableObject
{
    private readonly PanelViewModel _owner;
    private ValidationItemViewModel? _selectedItem;
    private bool _hasRun;
    private int _wallCount;
    private int _errorCount;
    private int _warningCount;
    private int _doubleCountGroups;

    internal ValidationViewModel(PanelViewModel owner)
    {
        _owner = owner;
        ValidateCommand = new RelayCommand(Validate);
        ConvertParallelCommand = new RelayCommand(ConvertParallel);
    }

    public ObservableCollection<ValidationItemViewModel> Items { get; } = [];

    public RelayCommand ValidateCommand { get; }

    /// <summary>Reemplaza por un eje cada grupo de muros paralelos que suma dos veces.</summary>
    public RelayCommand ConvertParallelCommand { get; }

    public bool HasRun
    {
        get => _hasRun;
        private set => SetField(ref _hasRun, value);
    }

    public int WallCount
    {
        get => _wallCount;
        private set => SetField(ref _wallCount, value);
    }

    public int ErrorCount
    {
        get => _errorCount;
        private set => SetField(ref _errorCount, value);
    }

    public int WarningCount
    {
        get => _warningCount;
        private set => SetField(ref _warningCount, value);
    }

    /// <summary>Grupos de muros paralelos a menos de 0,50 m (las dos caras de un mismo muro).</summary>
    public int DoubleCountGroups
    {
        get => _doubleCountGroups;
        private set
        {
            if (!SetField(ref _doubleCountGroups, value)) return;
            OnPropertyChanged(nameof(HasDoubleCount));
            OnPropertyChanged(nameof(DoubleCountText));
        }
    }

    public bool HasDoubleCount => _doubleCountGroups > 0;

    public string DoubleCountText => _doubleCountGroups == 1
        ? "Hay 1 grupo de muros paralelos a menos de 0,50 m: son las dos caras de un mismo muro y suman la pérdida dos veces."
        : $"Hay {_doubleCountGroups} grupos de muros paralelos a menos de 0,50 m: son las dos caras de un mismo muro y suman la pérdida dos veces.";

    public bool IsValid => HasRun && ErrorCount == 0;

    public bool HasItems => Items.Count > 0;

    public string WallsText => WallCount == 1 ? "1 muro" : $"{WallCount} muros";

    public string ErrorsText => ErrorCount == 1 ? "1 error" : $"{ErrorCount} errores";

    public string WarningsText => WarningCount == 1 ? "1 aviso" : $"{WarningCount} avisos";

    public string Summary => !HasRun
        ? "Todavía no se validó el plano."
        : $"{WallCount} muro(s) · {ErrorCount} error(es) · {WarningCount} aviso(s)";

    public string EmptyText => !HasRun ? "" : "Sin problemas: el plano está listo para calcular.";

    /// <summary>Al elegir un problema con handle, se selecciona la entidad y se encuadra en el dibujo.</summary>
    public ValidationItemViewModel? SelectedItem
    {
        get => _selectedItem;
        set
        {
            if (!SetField(ref _selectedItem, value) || value?.Handle is not { } handle) return;

            _owner.Run(() =>
            {
                _owner.Interaction.Show([handle], zoom: true);
                _owner.Walls.Focus(handle);
                _owner.Report(StatusKind.Info, $"Entidad {handle} seleccionada", "Encuadrada en el dibujo.");
            });
        }
    }

    public void Validate() => _owner.Run(() =>
    {
        _owner.Reload();
        if (ErrorCount > 0)
        {
            _owner.Report(StatusKind.Error, $"El plano tiene {ErrorCount} error(es)", "Elegir cada uno en la lista para ubicarlo en el dibujo.");
        }
        else if (HasDoubleCount)
        {
            _owner.Report(StatusKind.Warning, "Hay muros que suman la pérdida dos veces", DoubleCountText,
                new MessageAction("Convertir en ejes", ConvertParallelCommand));
        }
        else if (WarningCount > 0)
        {
            _owner.Report(StatusKind.Warning, $"Plano válido con {WarningCount} aviso(s)", "Revisarlos en la lista de «2 · Plano y validación».");
        }
        else
        {
            _owner.Report(StatusKind.Success, "Plano válido", $"{WallCount} muro(s), sin errores.");
        }
    });

    public void ConvertParallel() => _owner.Run(() =>
    {
        OperationResult<ParallelConversion> result = _owner.Service.ConvertParallelWalls();
        _owner.Reload();
        if (!result.Succeeded)
        {
            _owner.Report(result, "No se pudieron convertir los muros", "");
            return;
        }

        ParallelConversion c = result.Value!;
        if (c.FacesMoved == 0)
        {
            _owner.Report(StatusKind.Warning, "No se convirtió ningún muro", string.Join(" ", result.Warnings));
            return;
        }

        _owner.Report(result, "No se pudieron convertir los muros",
            c.AxesCreated == 1 ? "1 eje creado en lugar de las dos caras" : $"{c.AxesCreated} ejes creados en lugar de las caras",
            $"{c.FacesMoved} cara(s) pasaron a la capa WIFIHEAT_CARAS, que no se lee como muro.");
    });

    internal void Update(ValidationSummary summary)
    {
        _selectedItem = null;
        OnPropertyChanged(nameof(SelectedItem));
        Items.Clear();
        foreach (ValidationItem item in summary.Items.OrderBy(i => i.Severity)) Items.Add(new ValidationItemViewModel(item));

        WallCount = summary.WallCount;
        ErrorCount = summary.ErrorCount;
        WarningCount = summary.WarningCount;
        DoubleCountGroups = summary.Items.Count(i => i.Code == ValidationIssue.DoubleCount);
        HasRun = true;
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(EmptyText));
        OnPropertyChanged(nameof(WallsText));
        OnPropertyChanged(nameof(ErrorsText));
        OnPropertyChanged(nameof(WarningsText));
    }
}
