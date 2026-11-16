using System.Collections.ObjectModel;
using WifiHeat.Core.Materials;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.UI.ViewModels;

public sealed class MaterialOption(Material material)
{
    public string Id { get; } = material.Id;

    public string Name { get; } = material.Name;

    public double LossDb2_4 { get; } = material.LossDb2_4;

    public double LossDb5 { get; } = material.LossDb5;

    public string LossText { get; } =
        $"{material.Name}: {NumberText.Format(material.LossDb2_4, "0.#")} dB a 2,4 GHz · {NumberText.Format(material.LossDb5, "0.#")} dB a 5 GHz";

    public override string ToString() => Name;
}

public enum WallFilter
{
    All,
    MissingMaterial,
    Selected,
}

/// <summary>Fila de la tabla de muros. Cambiar material o espesor escribe en el dibujo a través del servicio.</summary>
public sealed class WallRowViewModel : ObservableObject
{
    private readonly WallsViewModel _owner;
    private WallInfo _info;
    private MaterialOption? _material;
    private string _thicknessText = "";
    private bool _isSelected;
    private bool _isFocused;
    private bool _isOnPath;
    private string _pathNote = "";
    private bool _loading;

    internal WallRowViewModel(WallsViewModel owner, WallInfo info, MaterialOption? material)
    {
        _owner = owner;
        _info = info;
        Apply(info, material);
    }

    public string Handle => _info.Handle;

    public string Layer => _info.Layer;

    public string LengthText => NumberText.Format(_info.LengthM, "0.00");

    public string? MaterialId => _info.MaterialId;

    public bool MissingMaterial => _info.MaterialId is null;

    /// <summary>Material escrito en el dibujo que no está en el catálogo.</summary>
    public bool UnknownMaterial => _info.MaterialId is not null && _material is null;

    public string MaterialNote => MissingMaterial ? "Sin material" : UnknownMaterial ? $"Material desconocido: {_info.MaterialId}" : _material!.LossText;

    public string MaterialPlaceholder => UnknownMaterial ? $"¿{_info.MaterialId}?" : "Sin material";

    public MaterialOption? Material
    {
        get => _material;
        set
        {
            if (!SetField(ref _material, value) || _loading) return;
            _owner.AssignRow(this, value);
        }
    }

    public string ThicknessText
    {
        get => _thicknessText;
        set
        {
            if (!SetField(ref _thicknessText, value) || _loading) return;
            _owner.AssignRowThickness(this, value);
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value) && !_loading) _owner.OnSelectionChanged();
        }
    }

    /// <summary>Muro señalado desde la lista de problemas.</summary>
    public bool IsFocused
    {
        get => _isFocused;
        internal set => SetField(ref _isFocused, value);
    }

    /// <summary>El trayecto del AP al último punto consultado atraviesa este muro.</summary>
    public bool IsOnPath
    {
        get => _isOnPath;
        private set => SetField(ref _isOnPath, value);
    }

    public string PathNote
    {
        get => _pathNote;
        private set => SetField(ref _pathNote, value);
    }

    internal void Apply(WallInfo info, MaterialOption? material)
    {
        _loading = true;
        _info = info;
        Material = material;
        ThicknessText = info.ThicknessM is { } t ? NumberText.Format(t, "0.###") : "";
        _loading = false;
        OnPropertyChanged(nameof(Layer));
        OnPropertyChanged(nameof(LengthText));
        OnPropertyChanged(nameof(MissingMaterial));
        OnPropertyChanged(nameof(UnknownMaterial));
        OnPropertyChanged(nameof(MaterialNote));
        OnPropertyChanged(nameof(MaterialPlaceholder));
    }

    internal void Revert() => Apply(_info, _owner.Option(_info.MaterialId));

    internal void SetSelected(bool selected)
    {
        _loading = true;
        IsSelected = selected;
        _loading = false;
    }

    internal void SetPath(bool onPath, string note)
    {
        IsOnPath = onPath;
        PathNote = note;
    }
}

/// <summary>Sección "Estructuras": tabla editable de muros, filtros, selección y asignación de material.</summary>
public sealed class WallsViewModel : ObservableObject
{
    private readonly PanelViewModel _owner;
    private readonly List<WallRowViewModel> _rows = [];
    private List<string> _extraSelection = [];
    private IReadOnlyList<WallCrossing> _path = [];
    private WallFilter _filter = WallFilter.All;
    private MaterialOption? _bulkMaterial;
    private string _bulkThicknessText = "";
    private WallRowViewModel? _focusedRow;

    internal WallsViewModel(PanelViewModel owner)
    {
        _owner = owner;
        PickInDrawingCommand = RelayCommand.Async(PickInDrawingAsync);
        ShowSelectionCommand = new RelayCommand(ShowSelection);
        SelectAllCommand = new RelayCommand(SelectAllVisible);
        ClearSelectionCommand = new RelayCommand(ClearSelection);
        AssignCommand = new RelayCommand(Assign);
        ShowRowCommand = new RelayCommand(row => ShowRow(row as WallRowViewModel));
    }

    public IReadOnlyList<MaterialOption> Materials => _owner.Materials;

    public IReadOnlyList<WallRowViewModel> Rows => _rows;

    public ObservableCollection<WallRowViewModel> VisibleRows { get; } = [];

    public RelayCommand PickInDrawingCommand { get; }

    public RelayCommand ShowSelectionCommand { get; }

    public RelayCommand SelectAllCommand { get; }

    public RelayCommand ClearSelectionCommand { get; }

    public RelayCommand AssignCommand { get; }

    public RelayCommand ShowRowCommand { get; }

    public WallFilter Filter
    {
        get => _filter;
        set
        {
            if (!SetField(ref _filter, value)) return;
            OnPropertyChanged(nameof(ShowAll));
            OnPropertyChanged(nameof(ShowMissing));
            OnPropertyChanged(nameof(ShowSelected));
            ApplyFilter();
        }
    }

    public bool ShowAll
    {
        get => _filter == WallFilter.All;
        set { if (value) Filter = WallFilter.All; }
    }

    public bool ShowMissing
    {
        get => _filter == WallFilter.MissingMaterial;
        set { if (value) Filter = WallFilter.MissingMaterial; }
    }

    public bool ShowSelected
    {
        get => _filter == WallFilter.Selected;
        set { if (value) Filter = WallFilter.Selected; }
    }

    public bool HasWalls => _rows.Count > 0;

    public bool HasVisibleRows => VisibleRows.Count > 0;

    public string EmptyText => _rows.Count == 0
        ? "No hay muros. Generar ejes en «Preparar plano», o seleccionar líneas en el dibujo y asignarles material."
        : _filter == WallFilter.MissingMaterial ? "Todos los muros tienen material."
        : _filter == WallFilter.Selected ? "No hay muros seleccionados."
        : "";

    public int MissingCount => _rows.Count(r => r.MissingMaterial);

    public int SelectedCount => _rows.Count(r => r.IsSelected);

    /// <summary>Entidades elegidas en el dibujo que todavía no son muros.</summary>
    public int ExtraSelectionCount => _extraSelection.Count;

    public bool HasSelection => SelectedCount + ExtraSelectionCount > 0;

    public string CountText => $"{_rows.Count} muro(s) · {MissingCount} sin material";

    public string SelectionText => (SelectedCount, ExtraSelectionCount) switch
    {
        (0, 0) => "Ningún muro seleccionado",
        (var n, 0) => $"{n} muro(s) seleccionado(s)",
        (0, var e) => $"{e} entidad(es) nueva(s) seleccionada(s)",
        var (n, e) => $"{n} muro(s) y {e} entidad(es) nueva(s) seleccionados",
    };

    public MaterialOption? BulkMaterial
    {
        get => _bulkMaterial;
        set
        {
            if (SetField(ref _bulkMaterial, value)) OnPropertyChanged(nameof(MaterialLossText));
        }
    }

    /// <summary>Vacío = conservar el espesor de cada muro.</summary>
    public string BulkThicknessText
    {
        get => _bulkThicknessText;
        set => SetField(ref _bulkThicknessText, value);
    }

    public string MaterialLossText => _bulkMaterial?.LossText ?? "Elegir un material para ver su pérdida en 2,4 y 5 GHz.";

    public WallRowViewModel? FocusedRow
    {
        get => _focusedRow;
        private set => SetField(ref _focusedRow, value);
    }

    public async Task PickInDrawingAsync()
    {
        await _owner.RunAsync(PanelMode.PickLines, async () =>
        {
            IReadOnlyList<string>? handles = await _owner.Interaction.PickEntitiesAsync("Seleccionar muros o líneas");
            if (handles is null)
            {
                _owner.Report(StatusKind.Info, "Selección cancelada");
                return;
            }

            var chosen = new HashSet<string>(handles, StringComparer.OrdinalIgnoreCase);
            foreach (WallRowViewModel row in _rows) row.SetSelected(chosen.Remove(row.Handle));
            _extraSelection = [.. chosen];
            OnSelectionChanged();
            _owner.Report(StatusKind.Info, SelectionText, ExtraSelectionCount > 0
                ? "Al asignarles material, las entidades nuevas pasan a la capa de muros."
                : "");
        });
    }

    public void ShowSelection()
    {
        string[] handles = SelectedHandles();
        if (handles.Length == 0)
        {
            _owner.Report(StatusKind.Warning, "No hay muros seleccionados");
            return;
        }

        _owner.Run(() => _owner.Interaction.Show(handles, zoom: true));
    }

    public void ShowRow(WallRowViewModel? row)
    {
        if (row is not null) _owner.Run(() => _owner.Interaction.Show([row.Handle], zoom: true));
    }

    public void SelectAllVisible()
    {
        foreach (WallRowViewModel row in VisibleRows) row.SetSelected(true);
        OnSelectionChanged();
    }

    public void ClearSelection()
    {
        foreach (WallRowViewModel row in _rows) row.SetSelected(false);
        _extraSelection = [];
        OnSelectionChanged();
    }

    public void Assign()
    {
        string[] handles = SelectedHandles();
        if (handles.Length == 0)
        {
            _owner.Report(StatusKind.Warning, "No hay muros seleccionados", "Marcar filas de la tabla o usar «Seleccionar en el dibujo…».");
            return;
        }

        if (_bulkMaterial is not { } material)
        {
            _owner.Report(StatusKind.Warning, "Falta el material", "Elegir el material a asignar.");
            return;
        }

        double? thickness = null;
        if (!string.IsNullOrWhiteSpace(BulkThicknessText))
        {
            if (!NumberText.TryParse(BulkThicknessText, out double value) || value <= 0)
            {
                _owner.Report(StatusKind.Error, "Espesor no válido", "Tiene que ser un número mayor que 0, en metros. Vacío conserva el de cada muro.");
                return;
            }

            thickness = value;
        }

        _owner.Run(() =>
        {
            FacePairSurvey pairs = _owner.Service.FindFacePairs(handles);
            if (!pairs.HasPairs)
            {
                AssignSelection(handles, material, thickness, joinFaces: false);
                return;
            }

            _owner.Report(StatusKind.Warning,
                pairs.PairCount == 1 ? "La selección tiene 1 par de caras de un mismo muro" : $"La selección tiene {pairs.PairCount} pares de caras de un mismo muro",
                "Asignadas por separado, la pérdida se suma dos veces. «Unir en ejes» crea un eje por muro con el espesor medido y deja las caras en su capa.",
                new MessageAction("Unir en ejes", new RelayCommand(() => _owner.Run(() => AssignSelection(handles, material, thickness, joinFaces: true)))),
                new MessageAction("Ignorar", new RelayCommand(() => _owner.Run(() => AssignSelection(handles, material, thickness, joinFaces: false)))));
        });
    }

    private void AssignSelection(string[] handles, MaterialOption material, double? thickness, bool joinFaces)
    {
        string thicknessText = thickness is { } t ? $"Espesor {NumberText.Format(t, "0.###")} m." : "Espesor conservado.";
        List<string> added = _extraSelection;
        if (joinFaces)
        {
            OperationResult<FaceJoin> joined = _owner.Service.AssignMaterialJoiningFaces(handles, material.Id, thickness);
            if (joined.Succeeded) _extraSelection = [];
            _owner.Reload();
            if (!joined.Succeeded)
            {
                _owner.Report(joined, "No se pudo asignar el material", "");
                return;
            }

            FaceJoin j = joined.Value!;
            Select([.. added, .. j.AxisHandles]);
            string range = j.MinThicknessM == j.MaxThicknessM
                ? NumberText.Format(j.MinThicknessM, "0.###")
                : $"{NumberText.Format(j.MinThicknessM, "0.###")} a {NumberText.Format(j.MaxThicknessM, "0.###")}";
            _owner.Report(joined, "No se pudo asignar el material",
                $"{j.AxisCount} eje(s) y {j.SingleWalls} muro(s) sin par con {material.Name}",
                $"Ejes a partir de {j.FaceCount} cara(s), que siguen en su capa. Espesor medido: {range} m.");
            return;
        }

        OperationResult<int> result = _owner.Service.AssignMaterial(handles, material.Id, thickness);
        if (result.Succeeded) _extraSelection = [];
        _owner.Reload();
        if (result.Succeeded) Select(added);
        _owner.Report(result, "No se pudo asignar el material", $"{result.Value} muro(s) con {material.Name}", thicknessText);
    }

    private void Select(IReadOnlyCollection<string> handles)
    {
        foreach (WallRowViewModel row in _rows.Where(r => handles.Contains(r.Handle, StringComparer.OrdinalIgnoreCase))) row.SetSelected(true);
        OnSelectionChanged();
    }

    internal MaterialOption? Option(string? materialId) =>
        materialId is null ? null : Materials.FirstOrDefault(m => string.Equals(m.Id, materialId, StringComparison.OrdinalIgnoreCase));

    internal void Update(IReadOnlyList<WallInfo> walls)
    {
        var existing = _rows.ToDictionary(r => r.Handle, StringComparer.OrdinalIgnoreCase);
        _rows.Clear();
        foreach (WallInfo wall in walls)
        {
            if (existing.TryGetValue(wall.Handle, out WallRowViewModel? row)) row.Apply(wall, Option(wall.MaterialId));
            else row = new WallRowViewModel(this, wall, Option(wall.MaterialId));
            _rows.Add(row);
        }

        if (_focusedRow is not null && !_rows.Contains(_focusedRow)) FocusedRow = null;
        ApplyPath();
        OnPropertyChanged(nameof(HasWalls));
        OnPropertyChanged(nameof(MissingCount));
        OnPropertyChanged(nameof(CountText));
        OnSelectionChanged();
    }

    /// <summary>Marca los muros que atraviesa el trayecto de la última consulta; null o vacío los desmarca.</summary>
    internal void MarkPath(IReadOnlyList<WallCrossing>? crossings)
    {
        _path = crossings ?? [];
        ApplyPath();
    }

    /// <summary>Destaca la fila del muro; si el filtro la oculta, vuelve a "todos".</summary>
    internal void Focus(string handle)
    {
        WallRowViewModel? row = _rows.FirstOrDefault(r => string.Equals(r.Handle, handle, StringComparison.OrdinalIgnoreCase));
        foreach (WallRowViewModel other in _rows) other.IsFocused = ReferenceEquals(other, row);
        if (row is not null && !VisibleRows.Contains(row)) Filter = WallFilter.All;
        FocusedRow = row;
    }

    internal void AssignRow(WallRowViewModel row, MaterialOption? material)
    {
        if (material is null)
        {
            row.Revert();
            return;
        }

        bool done = _owner.Run(() =>
        {
            OperationResult<int> result = _owner.Service.AssignMaterial([row.Handle], material.Id, null);
            _owner.Reload();
            _owner.Report(result, "No se pudo asignar el material", $"Muro {row.Handle}: {material.Name}");
        });
        if (!done) row.Revert();
    }

    internal void AssignRowThickness(WallRowViewModel row, string text)
    {
        if (!NumberText.TryParse(text, out double thickness) || thickness <= 0)
        {
            _owner.Report(StatusKind.Error, "Espesor no válido", "Tiene que ser un número mayor que 0, en metros.");
            row.Revert();
            return;
        }

        if (row.MaterialId is not { } materialId)
        {
            _owner.Report(StatusKind.Warning, "Falta el material", $"Elegir primero el material del muro {row.Handle}.");
            row.Revert();
            return;
        }

        bool done = _owner.Run(() =>
        {
            OperationResult<int> result = _owner.Service.AssignMaterial([row.Handle], materialId, thickness);
            _owner.Reload();
            _owner.Report(result, "No se pudo cambiar el espesor", $"Muro {row.Handle}: espesor {NumberText.Format(thickness, "0.###")} m");
        });
        if (!done) row.Revert();
    }

    internal void OnSelectionChanged()
    {
        OnPropertyChanged(nameof(SelectedCount));
        OnPropertyChanged(nameof(ExtraSelectionCount));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectionText));
        ApplyFilter();
    }

    private string[] SelectedHandles() => [.. _rows.Where(r => r.IsSelected).Select(r => r.Handle).Concat(_extraSelection)];

    private void ApplyFilter()
    {
        List<WallRowViewModel> wanted = [.. _rows.Where(r => _filter switch
        {
            WallFilter.MissingMaterial => r.MissingMaterial,
            WallFilter.Selected => r.IsSelected,
            _ => true,
        })];

        if (!wanted.SequenceEqual(VisibleRows))
        {
            VisibleRows.Clear();
            foreach (WallRowViewModel row in wanted) VisibleRows.Add(row);
        }

        OnPropertyChanged(nameof(HasVisibleRows));
        OnPropertyChanged(nameof(EmptyText));
    }

    private void ApplyPath()
    {
        var counted = _path.ToDictionary(c => c.WallId, StringComparer.OrdinalIgnoreCase);
        var inJunction = new HashSet<string>(_path.SelectMany(c => c.JunctionWith), StringComparer.OrdinalIgnoreCase);
        foreach (WallRowViewModel row in _rows)
        {
            if (counted.TryGetValue(row.Handle, out WallCrossing? crossing))
            {
                row.SetPath(true, $"En el trayecto al último punto consultado: suma {NumberText.Format(crossing.LossDb, "0.0")} dB.");
            }
            else if (inJunction.Contains(row.Handle))
            {
                row.SetPath(true, "En el trayecto al último punto consultado, en una unión: no suma (cuenta la pérdida mayor).");
            }
            else
            {
                row.SetPath(false, "");
            }
        }
    }
}
