using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WifiHeat.AutoCAD.UI.ViewModels;

namespace WifiHeat.AutoCAD.UI.Views;

/// <summary>Panel de WifiHeat. Solo enlaza con <see cref="PanelViewModel"/>; sin DataContext muestra "no hay dibujo".</summary>
public partial class PanelView : UserControl
{
    private const string ThemeFolder = "/WifiHeat.AutoCAD.UI;component/Views/Themes/";
    private WallsViewModel? _walls;

    public PanelView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    public bool IsDark { get; private set; } = true;

    /// <summary>Cambia entre el tema oscuro y el claro.</summary>
    public void ApplyTheme(bool dark)
    {
        if (dark == IsDark) return;

        IsDark = dark;
        Resources.MergedDictionaries[0] = new ResourceDictionary
        {
            Source = new Uri(ThemeFolder + (dark ? "Dark.xaml" : "Light.xaml"), UriKind.Relative),
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_walls is not null) _walls.PropertyChanged -= OnWallsPropertyChanged;
        _walls = (e.NewValue as PanelViewModel)?.Walls;
        if (_walls is not null) _walls.PropertyChanged += OnWallsPropertyChanged;

        bool hasDocument = e.NewValue is PanelViewModel;
        Body.Visibility = hasDocument ? Visibility.Visible : Visibility.Collapsed;
        NoDocument.Visibility = hasDocument ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Lleva a la vista la fila del muro señalado desde la lista de problemas.</summary>
    private void OnWallsPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(WallsViewModel.FocusedRow) || sender is not WallsViewModel { FocusedRow: { } row } walls) return;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            int index = walls.VisibleRows.IndexOf(row);
            if (index < 0) return;

            WallRows.BringIntoView();
            if (WallRows.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement container) container.BringIntoView();
            else FindChild<VirtualizingStackPanel>(WallRows)?.BringIndexIntoViewPublic(index);
        });
    }

    private static T? FindChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, i);
            if (child is T match) return match;
            if (FindChild<T>(child) is { } nested) return nested;
        }

        return null;
    }
}
