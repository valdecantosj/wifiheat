using System.Collections.ObjectModel;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.UI.ViewModels;

public enum MapState
{
    None,
    Current,
    Outdated,
}

public sealed class LegendEntryViewModel(SignalLevel level)
{
    public string Label { get; } = level.Label;

    public string ColorName { get; } = level.ColorName;

    public string ColorHex { get; } = $"#{level.R:X2}{level.G:X2}{level.B:X2}";
}

/// <summary>Sección "Calcular y mostrar": área de cobertura, cálculo, resumen, atenuación del mapa y leyenda.</summary>
public sealed class CalculationViewModel : ObservableObject
{
    private readonly PanelViewModel _owner;
    private MapState _state = MapState.None;
    private CalculationSummary? _summary;
    private string _areaText = "";
    private bool _hasCustomArea;
    private string _readiness = "";
    private int _fadePercent;
    private bool _loading;

    internal CalculationViewModel(PanelViewModel owner)
    {
        _owner = owner;
        _fadePercent = owner.Service.Settings.OverlayFadePercent;
        CalculateCommand = RelayCommand.Async(CalculateAsync);
        PickAreaCommand = RelayCommand.Async(PickAreaAsync);
        UseRectangleCommand = new RelayCommand(UseRectangle);
    }

    public IReadOnlyList<LegendEntryViewModel> Legend { get; } = [.. SignalScale.Levels.Select(l => new LegendEntryViewModel(l))];

    public ObservableCollection<string> Problems { get; } = [];

    public RelayCommand CalculateCommand { get; }

    public RelayCommand PickAreaCommand { get; }

    public RelayCommand UseRectangleCommand { get; }

    public int MinFade => OverlayFade.MinPercent;

    public int MaxFade => OverlayFade.MaxPercent;

    public MapState State
    {
        get => _state;
        private set
        {
            if (!SetField(ref _state, value)) return;
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsOutdated));
            OnPropertyChanged(nameof(IsCurrent));
        }
    }

    public bool IsOutdated => _state == MapState.Outdated;

    public bool IsCurrent => _state == MapState.Current;

    public string StateText => _state switch
    {
        MapState.Current => "Mapa al día",
        MapState.Outdated => "Mapa desactualizado: volver a calcular",
        _ => "Sin calcular",
    };

    /// <summary>Qué falta para poder calcular; vacío si está todo listo.</summary>
    public string Readiness
    {
        get => _readiness;
        private set
        {
            if (SetField(ref _readiness, value)) OnPropertyChanged(nameof(IsReady));
        }
    }

    public bool IsReady => _readiness.Length == 0;

    public bool HasSummary => _summary is not null;

    public string CellsText => _summary is { } s
        ? $"{s.Cells} ({s.Columns} × {s.Rows}, {NumberText.Format(s.CellSizeM, "0.00")} m)"
        : "";

    public string MinText => _summary is { } s ? $"{NumberText.Format(s.MinDbm, "0.0")} dBm" : "";

    public string MaxText => _summary is { } s ? $"{NumberText.Format(s.MaxDbm, "0.0")} dBm" : "";

    public string TimeText => _summary is { } s
        ? $"{NumberText.Format(s.CalculationMs, "0")} ms de cálculo + {NumberText.Format(s.ImageMs, "0")} ms de imagen"
        : "";

    public string AreaText
    {
        get => _areaText;
        private set => SetField(ref _areaText, value);
    }

    public bool HasCustomArea
    {
        get => _hasCustomArea;
        private set => SetField(ref _hasCustomArea, value);
    }

    public bool HasProblems => Problems.Count > 0;

    /// <summary>Atenuación del mapa en %. Se aplica al mapa ya dibujado, sin recalcular.</summary>
    public int FadePercent
    {
        get => _fadePercent;
        set
        {
            int previous = _fadePercent;
            if (!SetField(ref _fadePercent, value)) return;
            OnPropertyChanged(nameof(FadeText));
            if (_loading) return;

            bool done = _owner.Run(() =>
            {
                OperationResult<int> result = _owner.Service.SetOverlayFade(value);
                if (!_owner.Report(result, "No se pudo cambiar la atenuación", $"Atenuación del mapa: {value} %")) ShowFade(previous);
            });
            if (!done) ShowFade(previous);
        }
    }

    public string FadeText => $"{_fadePercent} %";

    public Task CalculateAsync() => _owner.RunBusyAsync("Calculando el mapa…", () =>
    {
        OperationResult<CalculationSummary> result = _owner.Service.Calculate();
        _owner.Reload();
        SetProblems([.. result.Errors, .. result.Warnings]);
        if (!result.Succeeded)
        {
            _owner.Report(StatusKind.Error, "No se pudo calcular el mapa", result.Errors.Count == 1 ? result.Errors[0] : $"{result.Errors.Count} problemas; ver la lista en «5 · Calcular y mostrar».");
            return;
        }

        CalculationSummary s = result.Value!;
        _owner.Report(result, "No se pudo calcular el mapa", "Mapa calculado", $"{s.Cells} celdas, RSSI de {NumberText.Format(s.MinDbm, "0.0")} a {NumberText.Format(s.MaxDbm, "0.0")} dBm.");
    });

    public async Task PickAreaAsync()
    {
        if (!_owner.RequireUnit()) return;

        await _owner.RunAsync(PanelMode.CoverageArea, async () =>
        {
            string? handle = await _owner.Interaction.PickPolylineAsync("Polilínea cerrada del área de cobertura");
            if (handle is null)
            {
                _owner.Report(StatusKind.Info, "Área de cobertura sin cambios", "Selección cancelada.");
                return;
            }

            OperationResult<CoverageAreaInfo> result = _owner.Service.SetCoverageArea(handle);
            _owner.Reload();
            _owner.Report(result, "No se pudo usar la polilínea", "Área de cobertura definida", result.Succeeded ? $"{Describe(result.Value!)}." : "");
        });
    }

    public void UseRectangle() => _owner.Run(() =>
    {
        OperationResult<CoverageAreaInfo> result = _owner.Service.ClearCoverageArea();
        _owner.Reload();
        _owner.Report(result, "No se pudo cambiar el área de cobertura", "Área de cobertura: rectángulo de los muros");
    });

    internal void Update()
    {
        IWifiHeatService service = _owner.Service;
        CoverageAreaInfo area = service.GetCoverageArea();
        HasCustomArea = area.IsCustom;
        AreaText = area.Warning is null ? Describe(area) : $"{Describe(area)}. {area.Warning}";

        _summary = service.LastCalculation;
        State = service.HasResult ? MapState.Current : _summary is null ? MapState.None : MapState.Outdated;
        OnPropertyChanged(nameof(HasSummary));
        OnPropertyChanged(nameof(CellsText));
        OnPropertyChanged(nameof(MinText));
        OnPropertyChanged(nameof(MaxText));
        OnPropertyChanged(nameof(TimeText));

        Readiness = !_owner.Prepare.HasUnit ? "Falta elegir la unidad del dibujo."
            : !_owner.Walls.HasWalls ? "No hay muros."
            : _owner.Validation.ErrorCount > 0 ? $"Corregir {_owner.Validation.ErrorCount} error(es) del plano."
            : !_owner.AccessPoint.HasAccessPoint ? "Falta colocar el AP."
            : "";
        if (_state == MapState.Current) SetProblems([.. Problems.Where(p => _summary!.Warnings.Contains(p))]);
        ShowFade(service.Settings.OverlayFadePercent);
    }

    private void SetProblems(IReadOnlyList<string> problems)
    {
        if (!problems.SequenceEqual(Problems))
        {
            Problems.Clear();
            foreach (string problem in problems) Problems.Add(problem);
        }

        OnPropertyChanged(nameof(HasProblems));
    }

    private void ShowFade(int percent)
    {
        _loading = true;
        FadePercent = percent;
        _loading = false;
    }

    private static string Describe(CoverageAreaInfo area) => area.IsCustom
        ? $"Polilínea {area.Handle} · {area.VertexCount} vértices · {NumberText.Format(area.AreaM2, "0.##")} m²"
        : "Rectángulo de los muros";
}
