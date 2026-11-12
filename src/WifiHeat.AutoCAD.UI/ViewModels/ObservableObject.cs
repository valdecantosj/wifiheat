using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace WifiHeat.AutoCAD.UI.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(name);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RelayCommand(Action<object?> execute) : ICommand
{
    public RelayCommand(Action execute) : this(_ => execute())
    {
    }

    /// <summary>La tarea no se espera: la operación informa sus propios errores.</summary>
    public static RelayCommand Async(Func<Task> execute) => new(_ => { _ = execute(); });

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);
}

/// <summary>Números escritos por el usuario: acepta coma o punto decimal y muestra con coma.</summary>
public static class NumberText
{
    public static readonly CultureInfo Es = CultureInfo.GetCultureInfo("es-AR");

    public static bool TryParse(string? text, out double value)
    {
        string normalized = (text ?? "").Trim().Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    public static string Format(double value, string format = "0.##") => value.ToString(format, Es);
}
