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
    long ExpectedSizeBytes,
    long WrittenBytes,
    IncrementalHash Hash,
    int ExpectedChunkIndex) : FileTransferState {
    public long WrittenBytes { get; set; } = WrittenBytes;
    public int ExpectedChunkIndex { get; set; } = ExpectedChunkIndex;
}
public sealed record SendInProgress(Stream Source, IncrementalHash Hash, int NextChunk) : FileTransferState;
public sealed record AwaitingAck(ReadOnlyMemory<byte> Hash) : FileTransferState;