namespace Sarmat.TelemetryMonitor.Models;

public sealed record StationDescriptor(string Name, string Color);

public sealed record StationSnapshot(
    int Status,
    long AgeMs,
    uint Sequence,
    double? Voltage,
    double? Current,
    int? Satellites,
    double? Hdop,
    double? Heading,
    double? Altitude,
    int? RuijieQuality,
    byte Flags);
