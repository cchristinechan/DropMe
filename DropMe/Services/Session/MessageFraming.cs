using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
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
    private const int HeaderLen = 4 + 1 + 1 + 4 + 2 + 4; // magic + ver + type + acknowledges + flags + len

    public static (byte[] header, byte[] body) FrameMessage(DropMeMsg msg, uint acknowledges) {
        var type = msg switch {
            PingMsg => SessionMessageType.Ping,
            PongMsg => SessionMessageType.Pong,
            DeviceNameMsg => SessionMessageType.DeviceName,
            FileOfferMsg => SessionMessageType.FileOffer,
            FileAcceptMsg => SessionMessageType.FileAccept,
            FileRejectMsg => SessionMessageType.FileReject,
            SwitchConnectionRequest => SessionMessageType.SwitchConnectionRequest,
            FileChunkMsg => SessionMessageType.FileChunk,
            FileDoneMsg => SessionMessageType.FileDone,
            FileAckMsg => SessionMessageType.FileAck,
            DisconnectMsg => SessionMessageType.Disconnect,
            _ => throw new ArgumentOutOfRangeException("Somehow created an invalid message type?")
        };
        var header = new byte[HeaderLen];
        header[0] = Magic[0]; header[1] = Magic[1]; header[2] = Magic[2]; header[3] = Magic[3];
        header[4] = Version;
        header[5] = (byte)type;
        header[6] = (byte)(acknowledges >> (8 * 3));
        header[7] = (byte)(acknowledges >> (8 * 2));
        header[8] = (byte)(acknowledges >> (8 * 1));
        header[9] = (byte)(acknowledges & 0xFF);
        header[10] = 0; header[11] = 0; // flags reserved

        // Need get type or it for some reason won't resolve the actual type of msg and always serialise it as {}
        var serialised = JsonSerializer.SerializeToUtf8Bytes(msg, msg.GetType());

        BinaryPrimitives.WriteUInt32LittleEndian(header.AsSpan()[8..(8 + 4)], (uint)serialised.Length);

        return (header, serialised);
    }

    public static (DropMeMsg, uint) ParseMessage(byte[] data) {
        var header = data.AsSpan()[0..HeaderLen];

        if (header[0] != Magic[0] || header[1] != Magic[1] || header[2] != Magic[2] || header[3] != Magic[3])
            throw new InvalidDataException("Bad magic.");
        if (header[4] != Version)
            throw new InvalidDataException($"Unsupported version: {header[4]}");

        var type = (SessionMessageType)header[5];
        var acknowledges = BinaryPrimitives.ReadUInt32BigEndian(header[6..(8 + 4)]);
        var len = BinaryPrimitives.ReadUInt32LittleEndian(header[12..(12 + 4)]);

        var span = data[HeaderLen..];
        return (type switch {
            SessionMessageType.Ping => JsonSerializer.Deserialize<PingMsg>(span),
            SessionMessageType.Pong => JsonSerializer.Deserialize<PongMsg>(span),
            SessionMessageType.DeviceName => JsonSerializer.Deserialize<DeviceNameMsg>(span),
            SessionMessageType.FileOffer => JsonSerializer.Deserialize<FileOfferMsg>(span),
            SessionMessageType.FileAccept => JsonSerializer.Deserialize<FileAcceptMsg>(span),
            SessionMessageType.FileReject => JsonSerializer.Deserialize<FileRejectMsg>(span),
            SessionMessageType.FileChunk => JsonSerializer.Deserialize<FileChunkMsg>(span),
            SessionMessageType.FileDone => JsonSerializer.Deserialize<FileDoneMsg>(span),
            SessionMessageType.FileAck => JsonSerializer.Deserialize<FileAckMsg>(span),
            SessionMessageType.SwitchConnectionRequest => JsonSerializer.Deserialize<SwitchConnectionRequest>(span),
            SessionMessageType.Disconnect => JsonSerializer.Deserialize<DisconnectMsg>(span),
            _ => throw new ArgumentOutOfRangeException("Somehow created an invalid message type?")
        }, acknowledges);
    }
}
