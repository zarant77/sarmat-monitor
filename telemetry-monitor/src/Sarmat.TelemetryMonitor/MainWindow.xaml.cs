using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Sarmat.TelemetryMonitor.ViewModels;

namespace Sarmat.TelemetryMonitor;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    private void SettingsClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel) viewModel.ShowSettings(this);
    }

    private void WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel) return;
        viewModel.RestoreWindow(this);
        ApplyColumnVisibility(viewModel.HiddenColumns);
    }

    private void WindowClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is MainViewModel viewModel) viewModel.RememberWindow(this);
    }

    private void ColumnsClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu { PlacementTarget = ColumnsButton, Placement = PlacementMode.Bottom };
        foreach (var column in StationsGrid.Columns)
        {
            var item = new MenuItem
            {
                Header = column.Header?.ToString() ?? column.SortMemberPath,
                IsCheckable = true,
                IsChecked = column.Visibility == Visibility.Visible,
                Tag = column
            };
            item.Click += ColumnVisibilityClick;
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    private void ColumnVisibilityClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: DataGridColumn column } item) return;
        column.Visibility = item.IsChecked ? Visibility.Visible : Visibility.Collapsed;
        if (DataContext is MainViewModel viewModel)
            viewModel.RememberHiddenColumns(StationsGrid.Columns
                .Where(value => value.Visibility != Visibility.Visible)
                .Select(value => value.SortMemberPath));
    }

    private void ApplyColumnVisibility(IReadOnlySet<string> hiddenColumns)
    {
        foreach (var column in StationsGrid.Columns)
            column.Visibility = hiddenColumns.Contains(column.SortMemberPath)
                ? Visibility.Collapsed : Visibility.Visible;
    }
}
