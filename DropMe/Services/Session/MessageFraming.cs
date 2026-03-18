using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services.Session;

public static class MessageFraming {
    private static readonly byte[] Magic = "DMS1"u8.ToArray();
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
            DisconnectMsg => SessionMessageType.Disconnect,
            _ => throw new ArgumentOutOfRangeException("Somehow created an invalid message type?")
        };
        Span<byte> header = stackalloc byte[HeaderLen];
        header[0] = Magic[0]; header[1] = Magic[1]; header[2] = Magic[2]; header[3] = Magic[3];
        header[4] = Version;
        header[5] = (byte)type;
        header[6] = 0; header[7] = 0; // flags reserved

        // Need get type or it for some reason won't resolve the actual type of msg and always serialise it as {}
        var serialised = JsonSerializer.SerializeToUtf8Bytes(msg, msg.GetType());

        BinaryPrimitives.WriteUInt32LittleEndian(header.Slice(8, 4), (uint)serialised.Length);

        await stream.WriteAsync(header.ToArray(), ct).ConfigureAwait(false);
        await stream.WriteAsync(serialised, ct).ConfigureAwait(false);
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
        var span = new ReadOnlySpan<byte>(payload);
        return type switch {
            SessionMessageType.Ping => JsonSerializer.Deserialize<PingMsg>(span),
            SessionMessageType.Pong => JsonSerializer.Deserialize<PongMsg>(span),
            SessionMessageType.FileOffer => JsonSerializer.Deserialize<FileOfferMsg>(span),
            SessionMessageType.FileAccept => JsonSerializer.Deserialize<FileAcceptMsg>(span),
            SessionMessageType.FileReject => JsonSerializer.Deserialize<FileRejectMsg>(span),
            SessionMessageType.FileChunk => JsonSerializer.Deserialize<FileChunkMsg>(span),
            SessionMessageType.FileDone => JsonSerializer.Deserialize<FileDoneMsg>(span),
            SessionMessageType.FileAck => JsonSerializer.Deserialize<FileAcceptMsg>(span),
            SessionMessageType.SwitchConnectionRequest => JsonSerializer.Deserialize<SwitchConnectionRequest>(span),
            SessionMessageType.SwitchConnectionAccept => JsonSerializer.Deserialize<SwitchConnectionAccept>(span),
            SessionMessageType.SwitchConnectionReject => JsonSerializer.Deserialize<SwitchConnectionReject>(span),
            SessionMessageType.Disconnect => JsonSerializer.Deserialize<DisconnectMsg>(span),
            _ => throw new ArgumentOutOfRangeException("Somehow created an invalid message type?")
        };
    }
}
