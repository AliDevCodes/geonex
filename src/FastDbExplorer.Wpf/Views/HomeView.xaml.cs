using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace FastDbExplorer.Wpf.Views;

public partial class HomeView : UserControl
{
    private static readonly (string Name, ResourceKey SystemKey)[] HighContrastRoles =
    [
        ("DashboardBackgroundBrush", SystemColors.WindowBrushKey),
        ("DashboardSurfaceBrush", SystemColors.WindowBrushKey),
        ("DashboardHoverBrush", SystemColors.ControlBrushKey),
        ("DashboardSoftBrush", SystemColors.ControlBrushKey),
        ("DashboardLineBrush", SystemColors.WindowTextBrushKey),
        ("DashboardOutlineBrush", SystemColors.WindowTextBrushKey),
        ("DashboardTextBrush", SystemColors.WindowTextBrushKey),
        ("DashboardMutedBrush", SystemColors.WindowTextBrushKey),
        ("DashboardAccentBrush", SystemColors.HighlightBrushKey),
        ("DashboardPrimaryBrush", SystemColors.HighlightBrushKey),
        ("DashboardPrimaryHoverBrush", SystemColors.HighlightBrushKey),
        ("DashboardPrimaryPressedBrush", SystemColors.HighlightBrushKey),
        ("DashboardOnPrimaryBrush", SystemColors.HighlightTextBrushKey),
        ("DashboardDisabledBrush", SystemColors.ControlBrushKey),
        ("DashboardDisabledTextBrush", SystemColors.GrayTextBrushKey),
        ("DashboardDangerBrush", SystemColors.WindowTextBrushKey)
    ];

    public HomeView() => InitializeComponent();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        SystemParameters.StaticPropertyChanged += OnSystemParametersChanged;
        UpdateHighContrast();
        UpdateLayoutMode();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e) =>
        SystemParameters.StaticPropertyChanged -= OnSystemParametersChanged;

    private void OnSystemParametersChanged(object? sender, PropertyChangedEventArgs e) =>
        Dispatcher.InvokeAsync(() => { if (IsLoaded) UpdateHighContrast(); });

    private void UpdateHighContrast()
    {
        foreach (var (name, systemKey) in HighContrastRoles)
        {
            if (SystemParameters.HighContrast) Resources[name] = FindResource(systemKey);
            else Resources.Remove(name);
        }
    }

    private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        DashboardContent.Margin = new Thickness(ActualWidth < 824 ? 20 : 32, 24, ActualWidth < 824 ? 20 : 32, 24);
    }

    private void OnContentSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.WidthChanged) UpdateLayoutMode();
    }

    private void UpdateLayoutMode()
    {
        var width = DashboardContent.ActualWidth;
        var stacked = width < 760;
        ExplorerColumn.Width = new GridLength(stacked ? 1 : width < 900 ? 58 : 62, GridUnitType.Star);
        SecondaryColumn.Width = stacked ? new GridLength(0) : new GridLength(width < 900 ? 42 : 38, GridUnitType.Star);
        PanelGap.Width = new GridLength(stacked ? 0 : 16);
        ExplorerPanel.Padding = new Thickness(width < 900 ? 24 : 28);
        Grid.SetColumn(SecondaryPanels, stacked ? 0 : 2);
        Grid.SetRow(SecondaryPanels, stacked ? 1 : 0);
        SecondaryPanels.Margin = new Thickness(0, stacked ? 16 : 0, 0, 0);
        Grid.SetColumn(SessionSummary, stacked ? 0 : 1);
        Grid.SetRow(SessionSummary, stacked ? 1 : 0);
        SessionSummary.Margin = stacked ? new Thickness(0, 12, 0, 0) : new Thickness(24, 4, 0, 0);
        Grid.SetColumn(AlertsAction, stacked ? 0 : 1);
        Grid.SetRow(AlertsAction, stacked ? 1 : 0);
        AlertsAction.Margin = stacked ? new Thickness(-4, 12, 0, 0) : new Thickness(16, -4, -4, 0);
    }
}
