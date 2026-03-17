namespace DropMe.Services.Session;

public abstract record DropMeMsg;
public abstract record ControlMsg :  DropMeMsg;
public abstract record DataMsg : DropMeMsg;
public sealed record HelloMsg(string Sid, string DeviceName) : ControlMsg;
public sealed record HelloAckMsg(bool Ok, string? Reason) : ControlMsg;
public sealed record FileOfferMsg(string FileId, string Name, long Size, uint ChunkSize) : ControlMsg;
public sealed record FileAcceptMsg(string FileId) : ControlMsg;
public sealed record FileRejectMsg(string FileId, string Reason) : ControlMsg;

// For chunks we’ll send encrypted bytes + tag + per-file nonce base in the first chunk
public sealed record FileChunkMsg(
    string FileId,
    uint Index,
    uint PlainLen,
    byte[] Nonce12,     // 12 bytes (base nonce with counter embedded)
    byte[] Cipher,      // PlainLen bytes
    byte[] Tag16        // 16 bytes
) : DataMsg;

public sealed record FileDoneMsg(string FileId) : DataMsg;