namespace DropMe.Services;

public sealed record FileTransferHeader(
    uint ChunkSize,
    long FileSize,
    string FileName,
    bool Encrypted,
    byte[]? BaseNonce // 12 bytes encrypted
);