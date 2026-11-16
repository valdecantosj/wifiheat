using WifiHeat.Core.Geometry;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Service;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.UI.ViewModels;

public sealed record BandOption(Band Band, string Label);

/// <summary>Sección "Punto de acceso": posición, banda, parámetros del enlace y resolución de la grilla.</summary>
public sealed class AccessPointViewModel : ObservableObject
{
    private readonly PanelViewModel _owner;
    private bool _loading;
    private bool _hasAccessPoint;
    private string _name = "";
    private string _xText = "";
    private string _yText = "";
    private BandOption _selectedBand;
    private string _txPowerText = "";
    private string _txGainText = "";
    private string _rxGainText = "";
    private string _txCableText = "";
    private string _rxCableText = "";
    private string _resolutionText = "";

    internal AccessPointViewModel(PanelViewModel owner)
    {
        _owner = owner;
        _selectedBand = Bands[0];
        PickCommand = RelayCommand.Async(PickAsync);
        ShowFields(owner.Service.Settings);
    }

    public IReadOnlyList<BandOption> Bands { get; } =
    [
        new(Band.Ghz2_4, BandInfo.Label(Band.Ghz2_4)),
        new(Band.Ghz5, BandInfo.Label(Band.Ghz5)),
    ];

    public RelayCommand PickCommand { get; }

    public bool HasAccessPoint
    {
        get => _hasAccessPoint;
        private set
        {
            if (SetField(ref _hasAccessPoint, value)) OnPropertyChanged(nameof(PositionSummary));
        }
    }

    public string PositionSummary => _hasAccessPoint ? $"{_name} en ({_xText}; {_yText}) m" : "Falta colocar el AP.";

    public string XText
    {
        get => _xText;
        set { if (SetField(ref _xText, value) && !_loading) ApplyPosition(); }
    }

    public string YText
    {
        get => _yText;
        set { if (SetField(ref _yText, value) && !_loading) ApplyPosition(); }
    }

    public BandOption SelectedBand
    {
        get => _selectedBand;
        set
        {
            if (value is null || !SetField(ref _selectedBand, value)) return;
            OnPropertyChanged(nameof(FrequencyText));
            if (!_loading) ApplySettings();
        }
    }

    public string FrequencyText => $"{NumberText.Format(BandInfo.RepresentativeFrequencyMHz(_selectedBand.Band), "0")} MHz";

    public string TxPowerText
    {
        get => _txPowerText;
        set { if (SetField(ref _txPowerText, value) && !_loading) ApplySettings(); }
    }

    public string TxGainText
    {
        get => _txGainText;
        set { if (SetField(ref _txGainText, value) && !_loading) ApplySettings(); }
    }

    public string RxGainText
    {
        get => _rxGainText;
        set { if (SetField(ref _rxGainText, value) && !_loading) ApplySettings(); }
    }

    public string TxCableText
    {
        get => _txCableText;
        set { if (SetField(ref _txCableText, value) && !_loading) ApplySettings(); }
    }

    public string RxCableText
    {
        get => _rxCableText;
        set { if (SetField(ref _rxCableText, value) && !_loading) ApplySettings(); }
    }

    public string ResolutionText
    {
        get => _resolutionText;
        set { if (SetField(ref _resolutionText, value) && !_loading) ApplySettings(); }
    }

    public async Task PickAsync()
    {
        if (!_owner.RequireUnit()) return;

        await _owner.RunAsync(PanelMode.PlaceAccessPoint, async () =>
        {
            Vec2? point = await _owner.Interaction.PickPointAsync("Ubicación del punto de acceso");
            if (point is null)
            {
                _owner.Report(StatusKind.Info, "Ubicación del punto de acceso cancelada");
                return;
            }

            Place(point.Value);
        });
    }

    internal void Update()
    {
        AccessPointInfo? ap = _owner.Service.AccessPoint;
        _loading = true;
        _name = ap?.Name ?? "";
        XText = ap is null ? "" : NumberText.Format(ap.PositionM.X, "0.###");
        YText = ap is null ? "" : NumberText.Format(ap.PositionM.Y, "0.###");
        _loading = false;
        HasAccessPoint = ap is not null;
        OnPropertyChanged(nameof(PositionSummary));
        ShowFields(_owner.Service.Settings);
    }

    private void ShowFields(SimulationSettings s)
    {
        _loading = true;
        SelectedBand = Bands.First(b => b.Band == s.Band);
        TxPowerText = NumberText.Format(s.TxPowerDbm, "0.##");
        TxGainText = NumberText.Format(s.TxAntennaGainDbi, "0.##");
        RxGainText = NumberText.Format(s.RxAntennaGainDbi, "0.##");
        TxCableText = NumberText.Format(s.TxCableLossDb, "0.##");
        RxCableText = NumberText.Format(s.RxCableLossDb, "0.##");
        ResolutionText = NumberText.Format(s.CellSizeM, "0.00");
        _loading = false;
    }

    private void ApplyPosition()
    {
        if (!_owner.RequireUnit())
        {
            Update();
            return;
        }

        if (string.IsNullOrWhiteSpace(_xText) || string.IsNullOrWhiteSpace(_yText))
        {
            if (!_hasAccessPoint) return;
        }

        if (!NumberText.TryParse(_xText, out double x) || !NumberText.TryParse(_yText, out double y))
        {
            _owner.Report(StatusKind.Error, "Posición no válida", "X e Y tienen que ser números, en metros.");
            if (_hasAccessPoint) Update();
            return;
        }

        if (!_owner.Run(() => Place(new Vec2(x, y)))) Update();
    }

    private void Place(Vec2 position)
    {
        OperationResult<AccessPointInfo> result = _owner.Service.SetAccessPoint(position);
        _owner.Reload();
        _owner.Report(result, "No se pudo ubicar el punto de acceso", "Punto de acceso ubicado", result.Succeeded ? $"{result.Value!.Name} en {result.Value.PositionM} m." : "");
    }

    private void ApplySettings()
    {
        SimulationSettings current = _owner.Service.Settings;
        (string Name, string Text)[] fields =
        [
            ("Potencia TX", _txPowerText), ("Ganancia de antena TX", _txGainText), ("Ganancia de antena RX", _rxGainText),
            ("Pérdida de cable TX", _txCableText), ("Pérdida de cable RX", _rxCableText), ("Resolución", _resolutionText),
        ];
        var values = new double[fields.Length];
        for (int i = 0; i < fields.Length; i++)
        {
            if (NumberText.TryParse(fields[i].Text, out values[i])) continue;

            _owner.Report(StatusKind.Error, $"{fields[i].Name} no válida", $"«{fields[i].Text}» no es un número. Se mantiene el valor anterior.");
            ShowFields(current);
            return;
        }

        SimulationSettings wanted = current with
        {
            Band = _selectedBand.Band,
            TxPowerDbm = values[0],
            TxAntennaGainDbi = values[1],
            RxAntennaGainDbi = values[2],
            TxCableLossDb = values[3],
            RxCableLossDb = values[4],
            CellSizeM = values[5],
        };
        if (wanted == current)
        {
            ShowFields(current);
            return;
        }

        bool done = _owner.Run(() =>
        {
            OperationResult<SimulationSettings> result = _owner.Service.Configure(wanted);
            _owner.Reload();
            if (!result.Succeeded) _owner.Report(StatusKind.Error, "Parámetros no válidos", $"{string.Join(" ", result.Errors)} Se mantienen los valores anteriores.");
            else if (_owner.Calculation.IsOutdated) _owner.Report(StatusKind.Warning, "Mapa desactualizado", "Los parámetros cambiaron. Volver a calcular el mapa.");
            else _owner.Report(result, "Parámetros no válidos", "Parámetros actualizados");
        });
        if (!done) ShowFields(current);
    }
}
