using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;

namespace DropMe.Services.Session;

public abstract record FileTransferState;
public sealed record AwaitingDecision(FileOfferInfo FileOffer, TaskCompletionSource<bool> DecisionTcs) : FileTransferState;

public sealed record ReceiveInProgress(
    Stream SaveStream,
    string SavePath,
    string OfferedName,
    long ExpectedSizeBytes,
    long WrittenBytes,
    IncrementalHash Hash,
    int ExpectedChunkIndex,
    bool Directory) : FileTransferState {
    public long WrittenBytes { get; set; } = WrittenBytes;
    public int ExpectedChunkIndex { get; set; } = ExpectedChunkIndex;
}
public sealed record SendInProgress(Stream Source, IncrementalHash Hash, int NextChunk) : FileTransferState;
public sealed record AwaitingAck(string FilePath, long FileSizeBytes, ReadOnlyMemory<byte> Hash) : FileTransferState;
