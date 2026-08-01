using System;
using System.IO;

namespace SarmatPlugin.Integration
{
    internal static class MessagePackTelemetryEncoder
    {
        public static byte[] Encode(uint sequence, double? voltage, double? current, int? satellites,
            double? hdop, double? heading, double? altitude, int? ruijieQuality, byte flags)
        {
            using (var output = new MemoryStream(80))
            {
                output.WriteByte(0x99); // fixed array with nine elements
                WriteUnsigned(output, sequence);
                WriteNullableDouble(output, voltage);
                WriteNullableDouble(output, current);
                WriteNullableInteger(output, satellites);
                WriteNullableDouble(output, hdop);
                WriteNullableDouble(output, heading);
                WriteNullableDouble(output, altitude);
                WriteNullableInteger(output, ruijieQuality);
                WriteUnsigned(output, flags);
                return output.ToArray();
            }
        }

        private static void WriteNullableDouble(Stream output, double? value)
        {
            if (!value.HasValue || double.IsNaN(value.Value) || double.IsInfinity(value.Value))
            {
                output.WriteByte(0xc0);
                return;
            }
            output.WriteByte(0xcb);
            var bytes = BitConverter.GetBytes(value.Value);
            if (BitConverter.IsLittleEndian) Array.Reverse(bytes);
            output.Write(bytes, 0, bytes.Length);
        }

        private static void WriteNullableInteger(Stream output, int? value)
        {
            if (!value.HasValue)
            {
                output.WriteByte(0xc0);
                return;
            }
            WriteUnsigned(output, unchecked((uint)value.Value));
        }

        private static void WriteUnsigned(Stream output, uint value)
        {
            if (value <= 0x7f)
            {
                output.WriteByte((byte)value);
                return;
            }
            if (value <= byte.MaxValue)
            {
                output.WriteByte(0xcc);
                output.WriteByte((byte)value);
                return;
            }
            if (value <= ushort.MaxValue)
            {
                output.WriteByte(0xcd);
                output.WriteByte((byte)(value >> 8));
                output.WriteByte((byte)value);
                return;
            }
            output.WriteByte(0xce);
            output.WriteByte((byte)(value >> 24));
            output.WriteByte((byte)(value >> 16));
            output.WriteByte((byte)(value >> 8));
            output.WriteByte((byte)value);
        }
    }
}
