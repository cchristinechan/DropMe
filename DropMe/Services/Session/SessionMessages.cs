using System;

namespace DropMe.Services.Session;

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
public sealed record FileAcceptMsg(Guid fileId) : ControlMsg;
public sealed record FileRejectMsg(Guid fileId, FileRejectReason Reason) : ControlMsg;
public sealed record SwitchConnectionRequest : ControlMsg;
public sealed record SwitchConnectionAccept : ControlMsg;
public sealed record SwitchConnectionReject : ControlMsg;
public sealed record Disconnect : ControlMsg;

// For chunks we’ll send encrypted bytes + tag + per-file nonce base in the first chunk
public sealed record FileChunkMsg(Guid fileId, int chunkIndex, ReadOnlyMemory<byte> data) : FileMsg(fileId);
public sealed record FileDoneMsg(Guid fileId, ReadOnlyMemory<byte> sha256) : FileMsg(fileId);
public sealed record FileAckMsg(Guid fileId, ReadOnlyMemory<byte> sha256) : FileMsg(fileId);