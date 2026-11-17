using System.Collections.ObjectModel;
using WifiHeat.Core.Geometry;
using WifiHeat.Core.Service;
using WifiHeat.Core.Simulation;
using WifiHeat.Core.Workflow;

namespace WifiHeat.AutoCAD.UI.ViewModels;

public sealed class CrossingViewModel(WallCrossing crossing)
{
    public string Handle { get; } = crossing.WallId;

    public string Material { get; } = crossing.MaterialName;

    public string LossText { get; } = $"{NumberText.Format(crossing.LossDb, "0.0")} dB";

    public string JunctionNote { get; } = crossing.JunctionWith.Count == 0
        ? ""
        : $"Unión con {string.Join(", ", crossing.JunctionWith)}: se cuenta una vez la pérdida mayor.";

    public bool HasJunction { get; } = crossing.JunctionWith.Count > 0;
}

/// <summary>Trayecto sumado en la consulta: descripción, longitud, pérdida y parte de la potencia.</summary>
public sealed class PathViewModel(PathContribution path)
{
    public string Description { get; } = Explanation.DescribePath(path);

    public string DetailText { get; } = $"{NumberText.Format(path.LengthM, "0.00")} m · pérdida {NumberText.Format(path.LossDb, "0.0")} dB";

    public string ShareText { get; } = $"{NumberText.Format(path.PowerShare * 100, "0.0")} %";
}

/// <summary>Sección "Consultar punto": tarjeta con la explicación del RSSI estimado en el punto elegido.</summary>
public sealed class QueryViewModel : ObservableObject
{
    private readonly PanelViewModel _owner;
    private QueryResult? _result;
    private Vec2? _point;
    private bool _isActive;

    internal QueryViewModel(PanelViewModel owner)
    {
        _owner = owner;
        QueryCommand = RelayCommand.Async(QueryAsync);
        ClearCommand = new RelayCommand(Clear);
        ShowWallsCommand = new RelayCommand(ShowWalls);
    }

    public ObservableCollection<CrossingViewModel> Crossings { get; } = [];

    /// <summary>Los trayectos que más aportan, con un modelo que suma varios; vacío con línea recta.</summary>
    public ObservableCollection<PathViewModel> Paths { get; } = [];

    public bool HasPaths => _result?.Calculation.Paths is not null;

    public string ModelText => _result is { } r ? PropagationModelInfo.Label(r.Calculation.Model) : "";

    public string PathsTitle => _result?.Calculation.Paths is { } paths
        ? paths.Count > Paths.Count ? $"Trayectos sumados: {paths.Count} (se muestran {Paths.Count})" : $"Trayectos sumados: {paths.Count}"
        : "";

    public string PathsNote => HasPaths
        ? "La distancia, L₀ y L materiales son los de la recta AP–punto. El RSSI es la suma en potencia de los trayectos."
        : "";

    public RelayCommand QueryCommand { get; }

    public RelayCommand ClearCommand { get; }

    public RelayCommand ShowWallsCommand { get; }

    /// <summary>True mientras la consulta está activa: cada clic en el dibujo actualiza la tarjeta hasta cancelar con Esc.</summary>
    public bool IsActive
    {
        get => _isActive;
        private set => SetField(ref _isActive, value);
    }

    public bool HasResult => _result is not null;

    public string EmptyText => "Todavía no se consultó ningún punto.";

    public string PointText => _result is { } r ? $"{r.PointM} m" : "";

    public string DistanceText
    {
        get
        {
            if (_result is not { } r) return "";
            PointCalculation c = r.Calculation;
            string distance = $"{NumberText.Format(c.DistanceM, "0.00")} m";
            return c.EffectiveDistanceM > c.DistanceM ? $"{distance} (se usa el mínimo de {NumberText.Format(c.EffectiveDistanceM)} m)" : distance;
        }
    }

    public string PathLossText => _result is { } r ? $"{NumberText.Format(r.Calculation.PathLossDb, "0.00")} dB" : "";

    public string WallLossText => _result is { } r ? $"{NumberText.Format(r.Calculation.WallLossDb, "0.00")} dB" : "";

    public string ReceivedText => _result is { } r ? $"{NumberText.Format(r.ReceivedPowerDbm, "0.00")} dBm" : "";

    public string LevelText => _result is { } r ? $"{r.Level.ColorName} ({r.Level.Label})" : "";

    public string LevelColorHex => _result is { } r ? $"#{r.Level.R:X2}{r.Level.G:X2}{r.Level.B:X2}" : "#00000000";

    public string CrossingsTitle => _result is { } r
        ? r.Calculation.Walls.Count == 0 ? "Muros cruzados: ninguno" : $"Muros cruzados: {r.Calculation.Walls.Count}"
        : "";

    public bool HasCrossings => Crossings.Count > 0;

    /// <summary>Texto completo de la explicación, el mismo que escribe WIFIQUERY.</summary>
    public string Explanation => _result?.Explanation ?? "";

    public async Task QueryAsync()
    {
        if (!_owner.RequireUnit()) return;

        await _owner.RunAsync(PanelMode.QueryPoint, async () =>
        {
            IsActive = true;
            try
            {
                int count = 0;
                while (await _owner.Interaction.PickPointAsync("Punto a consultar") is { } point)
                {
                    if (!Ask(point)) return;
                    count++;
                }

                if (count == 0) _owner.Report(StatusKind.Info, "Consulta cancelada");
            }
            finally
            {
                IsActive = false;
            }
        });
    }

    public void Clear()
    {
        _point = null;
        Show(null);
    }

    public void ShowWalls()
    {
        if (_result is null || _result.Calculation.Walls.Count == 0) return;

        string[] handles = [.. _result.Calculation.Walls.SelectMany(w => w.JunctionWith.Prepend(w.WallId)).Distinct()];
        _owner.Run(() => _owner.Interaction.Show(handles, zoom: false));
    }

    /// <summary>Repite la última consulta después de un cambio en el dibujo o en los parámetros.</summary>
    internal void Requery()
    {
        if (_point is not { } point) return;

        OperationResult<QueryResult> result = _owner.Service.Query(point);
        Show(result.Succeeded ? result.Value : null);
    }

    private bool Ask(Vec2 point)
    {
        OperationResult<QueryResult> result = _owner.Service.Query(point);
        if (!result.Succeeded)
        {
            _owner.Report(result, "No se pudo consultar el punto", "");
            return false;
        }

        _point = point;
        Show(result.Value);
        _owner.Report(StatusKind.Success, $"RSSI estimado: {ReceivedText}", $"En {point} m ({result.Value!.Level.ColorName}).");
        return true;
    }

    private void Show(QueryResult? result)
    {
        _result = result;
        Crossings.Clear();
        foreach (WallCrossing crossing in result?.Calculation.Walls ?? []) Crossings.Add(new CrossingViewModel(crossing));
        _owner.Walls.MarkPath(result?.Calculation.Walls);
        Paths.Clear();
        foreach (PathContribution path in (result?.Calculation.Paths ?? []).Take(Core.Workflow.Explanation.MaxListedPaths)) Paths.Add(new PathViewModel(path));

        OnPropertyChanged(nameof(HasResult));
        OnPropertyChanged(nameof(PointText));
        OnPropertyChanged(nameof(DistanceText));
        OnPropertyChanged(nameof(PathLossText));
        OnPropertyChanged(nameof(WallLossText));
        OnPropertyChanged(nameof(ReceivedText));
        OnPropertyChanged(nameof(LevelText));
        OnPropertyChanged(nameof(LevelColorHex));
        OnPropertyChanged(nameof(CrossingsTitle));
        OnPropertyChanged(nameof(HasCrossings));
        OnPropertyChanged(nameof(HasPaths));
        OnPropertyChanged(nameof(ModelText));
        OnPropertyChanged(nameof(PathsTitle));
        OnPropertyChanged(nameof(PathsNote));
        OnPropertyChanged(nameof(Explanation));
    }
}
