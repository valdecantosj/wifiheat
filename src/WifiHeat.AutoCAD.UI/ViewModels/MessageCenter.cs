using System.Collections.ObjectModel;

namespace WifiHeat.AutoCAD.UI.ViewModels;

public enum StatusKind
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>Acción que ofrece un mensaje: un botón dentro de la franja, mientras el mensaje sigue a la vista.</summary>
public sealed record MessageAction(string Label, RelayCommand Command);

/// <summary>Resultado de una acción del panel: título corto, detalle opcional y acciones sugeridas.</summary>
public sealed record PanelMessage(DateTime Time, StatusKind Kind, string Title, string Detail)
{
    public IReadOnlyList<MessageAction> Actions { get; init; } = [];

    public bool HasActions => Actions.Count > 0;

    public string Icon => Kind switch
    {
        StatusKind.Error => "✖",
        StatusKind.Warning => "⚠",
        StatusKind.Success => "✔",
        _ => "ℹ",
    };

    /// <summary>Nombre de la gravedad, para lectores de pantalla y para no depender del color.</summary>
    public string KindName => Kind switch
    {
        StatusKind.Error => "Error",
        StatusKind.Warning => "Advertencia",
        StatusKind.Success => "Listo",
        _ => "Información",
    };

    public string TimeText => Time.ToString("HH:mm:ss", NumberText.Es);

    public bool HasDetail => Detail.Length > 0;

    /// <summary>Error y advertencia se cierran a mano; éxito e información duran hasta la próxima acción.</summary>
    public bool CanDismiss => Kind is StatusKind.Error or StatusKind.Warning;

    public string Text => HasDetail ? $"{Title} {Detail}" : Title;

    public override string ToString() => HasDetail ? $"{TimeText} {Icon} {Title} — {Detail}" : $"{TimeText} {Icon} {Title}";
}

/// <summary>Mensaje actual del pie del panel e historial de los últimos <see cref="HistoryLimit"/>, el más nuevo primero.</summary>
public sealed class MessageCenter : ObservableObject
{
    public const int HistoryLimit = 50;

    private readonly Func<DateTime> _clock;
    private PanelMessage? _current;

    public MessageCenter(Func<DateTime>? clock = null)
    {
        _clock = clock ?? (() => DateTime.Now);
        DismissCommand = new RelayCommand(Dismiss);
        ClearHistoryCommand = new RelayCommand(ClearHistory);
    }

    public PanelMessage? Current
    {
        get => _current;
        private set
        {
            if (ReferenceEquals(_current, value)) return;

            _current = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasCurrent));
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(Text));
        }
    }

    public bool HasCurrent => Current is not null;

    public StatusKind Kind => Current?.Kind ?? StatusKind.Info;

    public string Text => Current?.Text ?? "";

    public ObservableCollection<PanelMessage> History { get; } = [];

    public bool HasHistory => History.Count > 0;

    public string HistoryText => string.Join(Environment.NewLine, History);

    public string HistorySummary => History.Count == 1 ? "1 mensaje" : $"{History.Count} mensajes";

    public RelayCommand DismissCommand { get; }

    public RelayCommand ClearHistoryCommand { get; }

    public void Post(StatusKind kind, string title, string detail = "", IReadOnlyList<MessageAction>? actions = null)
    {
        var message = new PanelMessage(_clock(), kind, title.Trim(), detail.Trim()) { Actions = actions ?? [] };
        History.Insert(0, message);
        while (History.Count > HistoryLimit) History.RemoveAt(History.Count - 1);
        OnHistoryChanged();
        Current = message;
    }

    /// <summary>Empieza otra acción del usuario: se quita el mensaje anterior, de cualquier gravedad.</summary>
    public void BeginAction() => Current = null;

    /// <summary>Cierra el error o la advertencia actual; el éxito y la información no se cierran a mano.</summary>
    public void Dismiss()
    {
        if (Current is { CanDismiss: true }) Current = null;
    }

    public void ClearHistory()
    {
        History.Clear();
        OnHistoryChanged();
    }

    private void OnHistoryChanged()
    {
        OnPropertyChanged(nameof(HasHistory));
        OnPropertyChanged(nameof(HistoryText));
        OnPropertyChanged(nameof(HistorySummary));
    }
}

/// <summary>Interacción con el dibujo en curso: nombre del modo e instrucción de una línea.</summary>
public sealed record PanelMode(string Name, string Instruction)
{
    public static PanelMode PickZone { get; } = new("Elegir zona de la planta", "Marcá con dos clics el rectángulo que encierra una sola planta.");

    public static PanelMode PickLines { get; } = new("Elegir líneas o muros", "Seleccioná líneas o polilíneas en el dibujo y terminá con Enter.");

    public static PanelMode PickFaces { get; } = new("Elegir líneas", "Seleccioná las líneas de las caras de los muros, de cualquier capa, y terminá con Enter.");

    public static PanelMode PickColumns { get; } = new("Elegir columnas", "Seleccioná los hatches o los contornos cerrados de las columnas y terminá con Enter.");

    public static PanelMode PlaceAccessPoint { get; } = new("Ubicar punto de acceso", "Hacé clic en el dibujo donde va el punto de acceso.");

    public static PanelMode QueryPoint { get; } = new("Consultar punto", "Cada clic en el dibujo muestra el RSSI estimado. Enter termina.");

    public static PanelMode CoverageArea { get; } = new("Definir área de cobertura", "Elegí en el dibujo una polilínea cerrada de tramos rectos.");

    public string Title => $"MODO: {Name.ToUpper(NumberText.Es)}";
}
