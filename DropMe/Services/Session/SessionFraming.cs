using System;
using System.Buffers.Binary;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services.Session;

public static class SessionFraming {
    private static readonly byte[] Magic = { (byte)'D', (byte)'M', (byte)'S', (byte)'1' };
    private const byte Version = 1;
    private const int HeaderLen = 4 + 1 + 1 + 2 + 4; // magic + ver + type + flags + len

    public static async Task WriteAsync(Stream stream, SessionMessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct = default) {
        Span<byte> header = stackalloc byte[HeaderLen];
        header[0] = Magic[0]; header[1] = Magic[1]; header[2] = Magic[2]; header[3] = Magic[3];
        header[4] = Version;
        header[5] = (byte)type;
        header[6] = 0; header[7] = 0; // flags reserved
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), (uint)payload.Length);

        await stream.WriteAsync(header.ToArray(), ct).ConfigureAwait(false);
        if (!payload.IsEmpty)
            await stream.WriteAsync(payload, ct).ConfigureAwait(false);

        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<(SessionMessageType type, byte[] payload)> ReadAsync(Stream stream, CancellationToken ct = default) {
        var header = new byte[HeaderLen];
        await stream.ReadExactlyAsync(header, 0, header.Length, ct).ConfigureAwait(false);

        if (header[0] != Magic[0] || header[1] != Magic[1] || header[2] != Magic[2] || header[3] != Magic[3])
            throw new InvalidDataException("Bad magic.");
        if (header[4] != Version)
            throw new InvalidDataException($"Unsupported version: {header[4]}");

        var type = (SessionMessageType)header[5];
        uint len = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));

        var payload = new byte[len];
        if (len > 0)
            await stream.ReadExactlyAsync(payload, 0, (int)len, ct).ConfigureAwait(false);

        return (type, payload);
    }
}
