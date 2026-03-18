using System;

namespace DropMe.Services.Session;

public enum SessionMessageType : byte {
    Ping = 3,
    Pong = 4,
    FileOffer = 10,
    FileAccept = 11,
    FileReject = 12,
    FileChunk = 13,
    FileDone = 14,
    FileAck = 15,
    SwitchConnectionRequest = 16,
    SwitchConnectionAccept = 17,
    SwitchConnectionReject = 18,
    Disconnect = 19,
}

public enum FileRejectReason {
    SizeMismatch,
    HashMismatch,
    UserRejected,
    InternalError
}

public abstract record DropMeMsg;
public abstract record ControlMsg : DropMeMsg;
public abstract record FileMsg(Guid FileId) : DropMeMsg;
public sealed record PingMsg : ControlMsg;
public sealed record PongMsg : ControlMsg;

public sealed record FileOfferMsg(FileOfferInfo Offer) : ControlMsg;
public sealed record FileAcceptMsg(Guid FileId) : ControlMsg;
public sealed record FileRejectMsg(Guid FileId, FileRejectReason Reason) : ControlMsg;
public sealed record SwitchConnectionRequest : ControlMsg;
public sealed record SwitchConnectionAccept : ControlMsg;
public sealed record SwitchConnectionReject : ControlMsg;
public sealed record DisconnectMsg : ControlMsg;

// For chunks we’ll send encrypted bytes + tag + per-file nonce base in the first chunk
public sealed record FileChunkMsg(Guid FileId, int ChunkIndex, ReadOnlyMemory<byte> Data) : FileMsg(FileId);
public sealed record FileDoneMsg(Guid FileId, ReadOnlyMemory<byte> Sha256) : FileMsg(FileId);
public sealed record FileAckMsg(Guid FileId, ReadOnlyMemory<byte> Sha256) : FileMsg(FileId);