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

    public static async Task WriteAsync(Stream stream, DropMeMsg msg, CancellationToken ct = default) {
        var type = msg switch {
            PingMsg => SessionMessageType.Ping,
            PongMsg => SessionMessageType.Pong,
            FileOfferMsg => SessionMessageType.FileOffer,
            FileAcceptMsg => SessionMessageType.FileAccept,
            FileRejectMsg => SessionMessageType.FileReject,
            SwitchConnectionRequest => SessionMessageType.SwitchConnectionRequest,
            SwitchConnectionAccept => SessionMessageType.SwitchConnectionAccept,
            SwitchConnectionReject => SessionMessageType.SwitchConnectionReject,
            FileChunkMsg => SessionMessageType.FileChunk,
            FileDoneMsg => SessionMessageType.FileDone,
            _ => throw new ArgumentOutOfRangeException("Somehow created an invalid message type?")
        };
        Span<byte> header = stackalloc byte[HeaderLen];
        header[0] = Magic[0]; header[1] = Magic[1]; header[2] = Magic[2]; header[3] = Magic[3];
        header[4] = Version;
        header[5] = (byte)type;
        header[6] = 0; header[7] = 0; // flags reserved
        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), (uint)msg.Payload.Length);

        await stream.WriteAsync(header.ToArray(), ct).ConfigureAwait(false);
        if (!msg.Payload.IsEmpty)
            await stream.WriteAsync(msg.Payload, ct).ConfigureAwait(false);

        await stream.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task<DropMeMsg> ReadAsync(Stream stream, CancellationToken ct = default) {
        var header = new byte[HeaderLen];
        await stream.ReadExactlyAsync(header, 0, header.Length, ct).ConfigureAwait(false);

        if (header[0] != Magic[0] || header[1] != Magic[1] || header[2] != Magic[2] || header[3] != Magic[3])
            throw new InvalidDataException("Bad magic.");
        if (header[4] != Version)
            throw new InvalidDataException($"Unsupported version: {header[4]}");

        var type = (SessionMessageType)header[5];
        var len = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));

        var payload = new byte[len];
        if (len > 0)
            await stream.ReadExactlyAsync(payload, 0, (int)len, ct).ConfigureAwait(false);

        return type switch {
            SessionMessageType.Ping => new PingMsg(),
            SessionMessageType.Pong => new PongMsg(),
            SessionMessageType.FileOffer => new FileOfferMsg(payload),
            SessionMessageType.FileAccept => new FileAcceptMsg(payload),
            SessionMessageType.FileReject => new FileRejectMsg(payload),
            SessionMessageType.FileChunk => new FileChunkMsg(payload),
            SessionMessageType.FileDone => new FileDoneMsg(payload),
            SessionMessageType.FileAck => new FileAcceptMsg(payload),
            SessionMessageType.SwitchConnectionRequest => new SwitchConnectionRequest(),
            SessionMessageType.SwitchConnectionAccept => new SwitchConnectionAccept(),
            SessionMessageType.SwitchConnectionReject => new SwitchConnectionReject(),
            _ => throw new ArgumentOutOfRangeException("Somehow created an invalid message type?")
        };
    }
}
