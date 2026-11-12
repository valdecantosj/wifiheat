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

    internal ValidationViewModel(PanelViewModel owner)
    {
        _owner = owner;
        ValidateCommand = new RelayCommand(Validate);
    }

    public ObservableCollection<ValidationItemViewModel> Items { get; } = [];

    public RelayCommand ValidateCommand { get; }

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

    public bool IsValid => HasRun && ErrorCount == 0;

    public bool HasItems => Items.Count > 0;

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
                _owner.Report(StatusKind.Info, $"Entidad {handle} seleccionada en el dibujo.");
            });
        }
    }

    public void Validate() => _owner.Run(() =>
    {
        _owner.Reload();
        if (ErrorCount > 0) _owner.Report(StatusKind.Error, $"El plano tiene {ErrorCount} error(es). Elegir cada uno en la lista para ubicarlo en el dibujo.");
        else if (WarningCount > 0) _owner.Report(StatusKind.Warning, $"Plano válido con {WarningCount} aviso(s).");
        else _owner.Report(StatusKind.Success, $"Plano válido: {WallCount} muro(s), sin errores.");
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
        HasRun = true;
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(EmptyText));
    }
}
