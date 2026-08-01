using System.IO;
using Sarmat.TelemetryMonitor.Models;

namespace Sarmat.TelemetryMonitor.Protocol;

internal static class MonitorProtocol
{
    public static object? Decode(ReadOnlySpan<byte> data)
    {
        var reader = new MessagePackReader(data);
        var value = reader.ReadValue();
        reader.EnsureComplete();
        return value;
    }

    public static IReadOnlyList<StationDescriptor> ReadConfiguration(object? value)
    {
        var root = Array(value, 2, "configuration");
        if (Integer(root[0], "protocol version") != 1)
            throw new InvalidDataException("Unsupported aggregator protocol version.");
        var stations = root[1] as object?[] ?? throw new InvalidDataException("Invalid station list.");
        return stations.Select((entry, index) =>
        {
            var station = Array(entry, 2, $"station {index}");
            return new StationDescriptor(String(station[0], "station name"),
                String(station[1], "station color"));
        }).ToArray();
    }

    public static IReadOnlyList<StationSnapshot?> ReadSnapshot(object? value, int stationCount)
    {
        var root = value as object?[] ?? throw new InvalidDataException("Snapshot must be an array.");
        if (root.Length != stationCount) throw new InvalidDataException("Snapshot station count changed.");
        return root.Select((entry, index) => entry is null ? null : ReadStation(entry, index)).ToArray();
    }

    private static StationSnapshot ReadStation(object value, int index)
    {
        var item = Array(value, 11, $"station snapshot {index}");
        return new StationSnapshot(
            checked((int)Integer(item[0], "status")),
            Integer(item[1], "age"),
            checked((uint)Integer(item[2], "sequence")),
            Number(item[3]), Number(item[4]), NullableInteger(item[5]), Number(item[6]),
            Number(item[7]), Number(item[8]), NullableInteger(item[9]),
            checked((byte)Integer(item[10], "flags")));
    }

    private static object?[] Array(object? value, int count, string name)
    {
        var array = value as object?[] ?? throw new InvalidDataException($"{name} must be an array.");
        if (array.Length != count) throw new InvalidDataException($"{name} must contain {count} elements.");
        return array;
    }

    private static long Integer(object? value, string name) => value is long number
        ? number : throw new InvalidDataException($"{name} must be an integer.");
    private static int? NullableInteger(object? value) => value is null ? null : checked((int)Integer(value, "value"));
    private static double? Number(object? value) => value switch
    {
        null => null,
        double number => number,
        float number => number,
        long number => number,
        _ => throw new InvalidDataException("Telemetry value must be numeric or nil.")
    };
    private static string String(object? value, string name) => value as string ??
        throw new InvalidDataException($"{name} must be a string.");
}
