namespace DropMe.Services.Session;

public sealed record HelloMsg(string Sid, string DeviceName);
public sealed record HelloAckMsg(bool Ok, string? Reason);

public sealed record FileOfferMsg(string FileId, string Name, long Size, uint ChunkSize);
public sealed record FileAcceptMsg(string FileId);
public sealed record FileRejectMsg(string FileId, string Reason);

// For chunks we’ll send encrypted bytes + tag + per-file nonce base in the first chunk
public sealed record FileChunkMsg(
    string FileId,
    uint Index,
    uint PlainLen,
    byte[] Nonce12,     // 12 bytes (base nonce with counter embedded)
    byte[] Cipher,      // PlainLen bytes
    byte[] Tag16        // 16 bytes
);

public sealed record FileDoneMsg(string FileId);