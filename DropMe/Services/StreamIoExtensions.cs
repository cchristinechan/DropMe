using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
namespace DropMe.Services;

public static class StreamIoExtensions {
    public static async Task ReadExactlyAsync(this Stream stream, byte[] buffer, int offset, int count, CancellationToken ct = default) {
        int total = 0;
        while (total < count) {
            int read = await stream.ReadAsync(buffer.AsMemory(offset + total, count - total), ct).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException($"Expected {count} bytes, got {total} bytes before EOF.");
            total += read;
        }
    }

    public static void WriteUInt32LE(this Span<byte> span, uint value) =>
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);

    public static uint ReadUInt32LE(this ReadOnlySpan<byte> span) =>
        BinaryPrimitives.ReadUInt32LittleEndian(span);

    public static async Task WriteUInt32LEAsync(this Stream stream, uint value, CancellationToken ct = default) {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(b, value);
        await stream.WriteAsync(b.ToArray(), ct).ConfigureAwait(false);
    }

    public static async Task<uint> ReadUInt32LEAsync(this Stream stream, CancellationToken ct = default) {
        var b = new byte[4];
        await stream.ReadExactlyAsync(b, 0, 4, ct).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt32LittleEndian(b);
    }

    public static async Task WriteUInt16LEAsync(this Stream stream, ushort value, CancellationToken ct = default) {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16LittleEndian(b, value);
        await stream.WriteAsync(b.ToArray(), ct).ConfigureAwait(false);
    }

    public static async Task<ushort> ReadUInt16LEAsync(this Stream stream, CancellationToken ct = default) {
        var b = new byte[2];
        await stream.ReadExactlyAsync(b, 0, 2, ct).ConfigureAwait(false);
        return BinaryPrimitives.ReadUInt16LittleEndian(b);
    }

    public static async Task WriteInt64LEAsync(this Stream stream, long value, CancellationToken ct = default) {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteInt64LittleEndian(b, value);
        await stream.WriteAsync(b.ToArray(), ct).ConfigureAwait(false);
    }

    public static async Task<long> ReadInt64LEAsync(this Stream stream, CancellationToken ct = default) {
        var b = new byte[8];
        await stream.ReadExactlyAsync(b, 0, 8, ct).ConfigureAwait(false);
        return BinaryPrimitives.ReadInt64LittleEndian(b);
    }

    public static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s);
}
