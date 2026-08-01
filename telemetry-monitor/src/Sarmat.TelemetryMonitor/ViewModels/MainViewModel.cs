using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Sarmat.TelemetryMonitor.Configuration;
using Sarmat.TelemetryMonitor.Models;
using Sarmat.TelemetryMonitor.Services;

namespace Sarmat.TelemetryMonitor.ViewModels;

public sealed class MainViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly MonitorConfig config;
    private TelemetryClient? client;
    private string connectionStatus = "Stopped";
    private string lastError = "";

    public ObservableCollection<StationViewModel> Stations { get; } = new();
    public IReadOnlySet<string> HiddenColumns => new HashSet<string>(config.HiddenColumns,
        StringComparer.OrdinalIgnoreCase);
    public string ConnectionStatus { get => connectionStatus; private set => Set(ref connectionStatus, value); }
    public string LastError { get => lastError; private set => Set(ref lastError, value); }

    public MainViewModel(MonitorConfig config)
    {
        this.config = config;
        CreateClient();
    }

    private void CreateClient()
    {
        client = new TelemetryClient(config);
        client.ConnectionChanged += (status, error) => Dispatch(() =>
        {
            ConnectionStatus = status;
            LastError = error ?? "";
        });
        client.ConfigurationReceived += descriptors => Dispatch(() => ApplyConfiguration(descriptors));
        client.SnapshotReceived += snapshots => Dispatch(() => ApplySnapshot(snapshots));
    }

    public void Start() => client?.Start();

    public void ShowSettings(Window owner)
    {
        var dialog = new SettingsWindow(config.AggregatorUrl, config.Secret, config.ReconnectSeconds)
        {
            Owner = owner
        };
        if (dialog.ShowDialog() != true) return;
        config.AggregatorUrl = dialog.AggregatorUrl;
        config.Secret = dialog.Secret;
        config.ReconnectSeconds = dialog.ReconnectSeconds;
        try
        {
            config.Save();
            client?.Dispose();
            CreateClient();
            client?.Start();
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Cannot save settings", MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    public void RestoreWindow(Window window)
    {
        window.Width = config.WindowWidth;
        window.Height = config.WindowHeight;
        if (config.WindowMaximized) window.WindowState = WindowState.Maximized;
    }

    public void RememberWindow(Window window)
    {
        var bounds = window.WindowState == WindowState.Maximized ? window.RestoreBounds :
            new Rect(window.Left, window.Top, window.Width, window.Height);
        config.WindowWidth = Math.Max(window.MinWidth, bounds.Width);
        config.WindowHeight = Math.Max(window.MinHeight, bounds.Height);
        config.WindowMaximized = window.WindowState == WindowState.Maximized;
        TrySaveConfig();
    }

    public void RememberHiddenColumns(IEnumerable<string> columns)
    {
        config.HiddenColumns = columns.Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        TrySaveConfig();
    }

    private void TrySaveConfig()
    {
        try { config.Save(); }
        catch (Exception ex)
        {
            LastError = "Cannot save settings: " + ex.Message;
        }
    }

    private void ApplyConfiguration(IReadOnlyList<StationDescriptor> descriptors)
    {
        Stations.Clear();
        foreach (var descriptor in descriptors) Stations.Add(new StationViewModel(descriptor));
    }

    private void ApplySnapshot(IReadOnlyList<StationSnapshot?> snapshots)
    {
        for (var index = 0; index < Stations.Count && index < snapshots.Count; index++)
            Stations[index].Update(snapshots[index]);
    }

    private static void Dispatch(Action action)
    {
        var dispatcher = Application.Current.Dispatcher;
        if (dispatcher.CheckAccess()) action(); else dispatcher.BeginInvoke(action);
    }

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Dispose() => client?.Dispose();
}
