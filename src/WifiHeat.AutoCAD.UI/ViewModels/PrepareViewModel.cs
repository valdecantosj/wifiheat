using System.Collections.ObjectModel;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Walls;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.UI.ViewModels;

public sealed record UnitOption(DrawingUnit Unit, string Label);

/// <summary>Capa con caras de muro dentro de la zona de la planta elegida.</summary>
public sealed class AxisLayerViewModel(AxisLayerInfo info, Action changed) : ObservableObject
{
    private bool _isSelected = info.IsSuggested;

    public string Name { get; } = info.Layer;

    public int SegmentCount { get; } = info.SegmentCount;

    public int AxisCount { get; } = info.AxisCount;

    public bool IsSuggested { get; } = info.IsSuggested;

    public bool ExplicitOnly { get; } = info.ExplicitOnly;

    public string CountText { get; } = $"{info.SegmentCount} segmento(s) · {info.AxisCount} eje(s)";

    public string Note { get; } = info.IsSuggested ? "sugerida" : info.ExplicitOnly ? "solo si se elige" : "";

    public string NoteSuffix => Note.Length == 0 ? "" : $" ({Note})";

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value)) changed();
        }
    }
}

/// <summary>Sección "Preparar plano": unidad del dibujo y generación de ejes de muro.</summary>
public sealed class PrepareViewModel : ObservableObject
{
    private readonly PanelViewModel _owner;
    private UnitOption? _selectedUnit;
    private string _unitHint = "";
    private bool _loading;
    private BoundingBox? _window;
    private string _windowText = "";
    private string _mergeGapText = NumberText.Format(AxisDefaults.MergeGapM, "0.00");
    private string _axisSummary = "";
    private IReadOnlyList<string> _unpaired = [];
    private IReadOnlyList<string> _smallFigures = [];

    internal PrepareViewModel(PanelViewModel owner)
    {
        _owner = owner;
        PickWindowCommand = RelayCommand.Async(PickWindowAsync);
        GenerateAxesCommand = new RelayCommand(GenerateAxes);
        PickLinesCommand = RelayCommand.Async(PickLinesAsync);
        ShowUnpairedCommand = new RelayCommand(ShowUnpaired);
        MarkColumnsCommand = new RelayCommand(MarkColumns);
        ShowSmallFiguresCommand = new RelayCommand(ShowSmallFigures);
    }

    public IReadOnlyList<UnitOption> Units { get; } =
    [
        new(DrawingUnit.Metres, "Metros"),
        new(DrawingUnit.Centimetres, "Centímetros"),
        new(DrawingUnit.Millimetres, "Milímetros"),
    ];

    public ObservableCollection<AxisLayerViewModel> Layers { get; } = [];

    public RelayCommand PickWindowCommand { get; }

    public RelayCommand GenerateAxesCommand { get; }

    /// <summary>Genera ejes solo entre las líneas elegidas en el dibujo, sin importar su capa.</summary>
    public RelayCommand PickLinesCommand { get; }

    public RelayCommand ShowUnpairedCommand { get; }

    public UnitOption? SelectedUnit
    {
        get => _selectedUnit;
        set
        {
            UnitOption? previous = _selectedUnit;
            if (!SetField(ref _selectedUnit, value) || _loading || value is null) return;

            bool done = _owner.Run(() =>
            {
                OperationResult<DrawingUnit> result = _owner.Service.SetDrawingUnit(value.Unit);
                _owner.Reload();
                _owner.Report(result, "No se pudo guardar la unidad", $"Unidad del dibujo: {value.Label.ToLowerInvariant()}");
            });
            if (!done) Restore(previous);
        }
    }

    public bool HasUnit => _selectedUnit is not null;

    public string UnitHint
    {
        get => _unitHint;
        private set => SetField(ref _unitHint, value);
    }

    public bool HasWindow => _window is not null;

    public string WindowText
    {
        get => _windowText;
        private set => SetField(ref _windowText, value);
    }

    public bool HasLayers => Layers.Count > 0;

    public bool CanGenerate => _window is not null && Layers.Any(l => l.IsSelected);

    public string MergeGapText
    {
        get => _mergeGapText;
        set => SetField(ref _mergeGapText, value);
    }

    public string AxisSummary
    {
        get => _axisSummary;
        private set
        {
            if (SetField(ref _axisSummary, value)) OnPropertyChanged(nameof(HasAxisSummary));
        }
    }

    public bool HasAxisSummary => _axisSummary.Length > 0;

    /// <summary>Figuras cerradas pequeñas que la generación de ejes dejó afuera: posibles columnas.</summary>
    public bool HasSmallFigures => _smallFigures.Count > 0;

    public string SmallFiguresText => _smallFigures.Count == 1
        ? "Se detectó 1 figura cerrada pequeña (posible columna)."
        : $"Se detectaron {_smallFigures.Count} figuras cerradas pequeñas (posibles columnas).";

    public RelayCommand MarkColumnsCommand { get; }

    public RelayCommand ShowSmallFiguresCommand { get; }

    public bool HasUnpaired => _unpaired.Count > 0;

    public string UnpairedText => _unpaired.Count == 0 ? "" : $"Ver {_unpaired.Count} segmento(s) sin par";

    internal void Update(DrawingSetup setup)
    {
        _loading = true;
        SelectedUnit = setup.Unit is { } unit ? Units.First(u => u.Unit == unit) : null;
        _loading = false;
        OnPropertyChanged(nameof(HasUnit));
        UnitHint = setup.Unit is null
            ? $"Falta elegir la unidad. El dibujo declara {setup.DeclaredUnits} (sugerida: {DrawingUnits.Label(setup.SuggestedUnit)}); comprobar midiendo un muro."
            : $"El dibujo declara {setup.DeclaredUnits}.";
    }

    public async Task PickWindowAsync()
    {
        if (!_owner.RequireUnit()) return;
        if (!TryGetMergeGap(out double gap)) return;

        await _owner.RunAsync(PanelMode.PickZone, async () =>
        {
            BoundingBox? window = await _owner.Interaction.PickWindowAsync("Zona de la planta");
            if (window is null)
            {
                _owner.Report(StatusKind.Info, "Selección de la zona cancelada");
                return;
            }

            OperationResult<AxisSurvey> survey = _owner.Service.SurveyAxisLayers(window.Value, gap);
            if (!survey.Succeeded)
            {
                SetWindow(null, []);
                _owner.Report(survey, "No se pudo leer la zona de la planta", "");
                return;
            }

            SetWindow(window, survey.Value!.Layers);
            _owner.Report(StatusKind.Info, $"{Layers.Count} capa(s) en la zona de la planta", survey.Value.SuggestedLayer is { } suggested
                ? $"Sugerida: {suggested}. Revisar las capas marcadas y generar los ejes."
                : "Marcar las capas de las caras de muro y generar los ejes.");
        });
    }

    public void GenerateAxes()
    {
        if (_window is not { } window)
        {
            _owner.Report(StatusKind.Warning, "Falta la zona de la planta", "Usar «Elegir zona de la planta…» antes de generar los ejes.");
            return;
        }

        string[] layers = [.. Layers.Where(l => l.IsSelected).Select(l => l.Name)];
        if (layers.Length == 0)
        {
            _owner.Report(StatusKind.Warning, "No hay capas marcadas", "Marcar al menos una capa con caras de muro.");
            return;
        }

        if (!TryGetMergeGap(out double gap)) return;

        _owner.Run(() =>
        {
            OperationResult<AxisGeneration> result = _owner.Service.GenerateAxes(window, layers, gap);
            if (!result.Succeeded)
            {
                _owner.Report(result, "No se generaron ejes", "");
                return;
            }

            ShowGeneration(result, "No hay pares de caras paralelas en las capas marcadas.");
        });
    }

    public async Task PickLinesAsync()
    {
        if (!_owner.RequireUnit()) return;
        if (!TryGetMergeGap(out double gap)) return;

        await _owner.RunAsync(PanelMode.PickFaces, async () =>
        {
            IReadOnlyList<string>? handles = await _owner.Interaction.PickEntitiesAsync("Seleccionar las líneas de las caras de los muros");
            if (handles is null)
            {
                _owner.Report(StatusKind.Info, "Selección de líneas cancelada");
                return;
            }

            if (handles.Count == 0)
            {
                _owner.Report(StatusKind.Warning, "No se eligió ninguna línea", "Elegir las líneas de las dos caras de cada muro.");
                return;
            }

            OperationResult<AxisGeneration> result = _owner.Service.GenerateAxesFromSelection(handles, gap);
            if (!result.Succeeded)
            {
                _owner.Report(result, "No se generaron ejes", "");
                return;
            }

            ShowGeneration(result, "No hay pares de caras paralelas entre las líneas elegidas.");
        });
    }

    public void MarkColumns()
    {
        if (_smallFigures.Count > 0 && _owner.Walls.MarkColumns(_smallFigures)) SetSmallFigures([]);
    }

    public void ShowSmallFigures()
    {
        if (_smallFigures.Count > 0) _owner.Run(() => _owner.Interaction.Show(_smallFigures, zoom: true));
    }

    private void ShowGeneration(OperationResult<AxisGeneration> result, string noPairs)
    {
        AxisGeneration g = result.Value!;
        _unpaired = [.. g.Unpaired.Concat(g.PartlyUnpaired).Select(id => id.Split('/')[0]).Distinct()];
        OnPropertyChanged(nameof(HasUnpaired));
        OnPropertyChanged(nameof(UnpairedText));
        SetSmallFigures(g.SmallClosedFigures);
        AxisSummary = g.AxisCount == 0
            ? "Sin ejes: no hay pares de caras paralelas."
            : $"{g.AxisCount} eje(s): {g.Created} creado(s), {g.Kept} conservado(s), {g.Removed} reemplazado(s). " +
              $"Espesor {NumberText.Format(g.MinThicknessM, "0.###")} a {NumberText.Format(g.MaxThicknessM, "0.###")} m. Falta asignarles material.";
        _owner.Reload();

        MessageAction[] actions = HasSmallFigures ? [new MessageAction("Marcar como columnas", MarkColumnsCommand)] : [];
        string columns = HasSmallFigures ? $" {SmallFiguresText}" : "";
        if (g.AxisCount == 0)
        {
            _owner.Report(StatusKind.Warning, "No se generaron ejes", string.Join(" ", result.Warnings.DefaultIfEmpty(noPairs)) + columns, actions);
        }
        else
        {
            _owner.Report(result, "No se generaron ejes", $"{g.AxisCount} eje(s) de muro generados",
                $"Falta asignarles material en «3 · Estructuras».{columns}", actions);
        }
    }

    private void SetSmallFigures(IReadOnlyList<string> handles)
    {
        _smallFigures = handles;
        OnPropertyChanged(nameof(HasSmallFigures));
        OnPropertyChanged(nameof(SmallFiguresText));
    }

    public void ShowUnpaired()
    {
        if (_unpaired.Count > 0) _owner.Run(() => _owner.Interaction.Show(_unpaired, zoom: true));
    }

    private bool TryGetMergeGap(out double gap)
    {
        if (NumberText.TryParse(MergeGapText, out gap) && AxisDetectionOptions.MergeGapError(gap) is null) return true;

        _owner.Report(StatusKind.Error, "Umbral de unión no válido", AxisDetectionOptions.MergeGapError(double.NaN)!);
        return false;
    }

    private void SetWindow(BoundingBox? window, IReadOnlyList<AxisLayerInfo> layers)
    {
        _window = window;
        Layers.Clear();
        foreach (AxisLayerInfo layer in layers) Layers.Add(new AxisLayerViewModel(layer, () => OnPropertyChanged(nameof(CanGenerate))));
        WindowText = window is { } w ? $"Zona de la planta: de {w.Min} a {w.Max} m" : "";
        OnPropertyChanged(nameof(HasWindow));
        OnPropertyChanged(nameof(HasLayers));
        OnPropertyChanged(nameof(CanGenerate));
    }

    private void Restore(UnitOption? previous)
    {
        _loading = true;
        SelectedUnit = previous;
        _loading = false;
    }
}
