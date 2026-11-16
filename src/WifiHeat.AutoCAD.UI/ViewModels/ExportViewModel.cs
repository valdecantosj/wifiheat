using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.UI.ViewModels;

/// <summary>Sección "Exportar PNG".</summary>
public sealed class ExportViewModel : ObservableObject
{
    private readonly PanelViewModel _owner;
    private string _lastPath = "";

    internal ExportViewModel(PanelViewModel owner)
    {
        _owner = owner;
        ExportCommand = new RelayCommand(Export);
    }

    public RelayCommand ExportCommand { get; }

    public string LastPath
    {
        get => _lastPath;
        private set
        {
            if (SetField(ref _lastPath, value)) OnPropertyChanged(nameof(HasPath));
        }
    }

    public bool HasPath => _lastPath.Length > 0;

    public string EmptyText => "Todavía no se exportó ninguna imagen.";

    public void Export() => _owner.Run(() =>
    {
        if (!_owner.Service.HasResult)
        {
            _owner.Report(StatusKind.Warning, "No hay mapa para exportar", _owner.Calculation.IsOutdated
                ? "El mapa está desactualizado. Volver a calcular antes de exportar."
                : "Primero calcular el mapa.");
            return;
        }

        string? path = _owner.Interaction.AskSavePath(_owner.Service.SuggestedExportPath);
        if (path is null)
        {
            _owner.Report(StatusKind.Info, "Exportación cancelada");
            return;
        }

        OperationResult<string> result = _owner.Service.ExportPng(path);
        if (result.Succeeded) LastPath = result.Value!;
        _owner.Report(result, "No se pudo exportar la imagen", "Imagen exportada", result.Succeeded ? result.Value! : "");
    });
}
