using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Sarmat.TelemetryMonitor.Models;

namespace Sarmat.TelemetryMonitor.ViewModels;

public sealed class StationViewModel : INotifyPropertyChanged
{
    private string status = "Waiting";
    private string voltage = "—";
    private string current = "—";
    private string satellites = "—";
    private string hdop = "—";
    private string heading = "—";
    private string altitude = "—";
    private string ruijie = "—";
    private string obs = "—";
    private string armed = "—";
    private string age = "—";
    private string sequence = "—";

    public string Name { get; }
    public Brush ColorBrush { get; }
    public string Status { get => status; private set => Set(ref status, value); }
    public string Voltage { get => voltage; private set => Set(ref voltage, value); }
    public string Current { get => current; private set => Set(ref current, value); }
    public string Satellites { get => satellites; private set => Set(ref satellites, value); }
    public string Hdop { get => hdop; private set => Set(ref hdop, value); }
    public string Heading { get => heading; private set => Set(ref heading, value); }
    public string Altitude { get => altitude; private set => Set(ref altitude, value); }
    public string Ruijie { get => ruijie; private set => Set(ref ruijie, value); }
    public string Obs { get => obs; private set => Set(ref obs, value); }
    public string Armed { get => armed; private set => Set(ref armed, value); }
    public string Age { get => age; private set => Set(ref age, value); }
    public string Sequence { get => sequence; private set => Set(ref sequence, value); }

    public StationViewModel(StationDescriptor descriptor)
    {
        Name = descriptor.Name;
        try { ColorBrush = (Brush)new BrushConverter().ConvertFromString(descriptor.Color)!; }
        catch { ColorBrush = Brushes.Gray; }
    }

    public void Update(StationSnapshot? snapshot)
    {
        if (snapshot is null)
        {
            Status = "Waiting";
            return;
        }
        Status = snapshot.Status switch { 0 => "Online", 1 => "Stale", 2 => "Offline", _ => "Unknown" };
        Voltage = Format(snapshot.Voltage, "0.0", " V");
        Current = Format(snapshot.Current, "0.0", " A");
        Satellites = snapshot.Satellites?.ToString(CultureInfo.InvariantCulture) ?? "—";
        Hdop = Format(snapshot.Hdop, "0.00", "");
        Heading = Format(snapshot.Heading, "0.0", "°");
        Altitude = Format(snapshot.Altitude, "0.0", " m");
        Ruijie = snapshot.RuijieQuality is null ? "—" : snapshot.RuijieQuality + "%";
        Obs = (snapshot.Flags & 1) != 0 ? "REC" : "Off";
        Armed = (snapshot.Flags & 2) != 0 ? "ARMED" : "Disarmed";
        Age = snapshot.AgeMs < 1000 ? snapshot.AgeMs + " ms" :
            (snapshot.AgeMs / 1000d).ToString("0.0", CultureInfo.InvariantCulture) + " s";
        Sequence = snapshot.Sequence.ToString(CultureInfo.InvariantCulture);
    }

    private static string Format(double? value, string format, string suffix) => value.HasValue
        ? value.Value.ToString(format, CultureInfo.InvariantCulture) + suffix : "—";
    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
