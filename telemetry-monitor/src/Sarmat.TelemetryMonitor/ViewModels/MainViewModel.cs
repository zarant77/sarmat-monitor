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
    private readonly TelemetryClient client;
    private string connectionStatus = "Stopped";
    private string lastError = "";

    public ObservableCollection<StationViewModel> Stations { get; } = new();
    public string ConnectionStatus { get => connectionStatus; private set => Set(ref connectionStatus, value); }
    public string LastError { get => lastError; private set => Set(ref lastError, value); }

    public MainViewModel(MonitorConfig config)
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

    public void Start() => client.Start();

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
    public void Dispose() => client.Dispose();
}
