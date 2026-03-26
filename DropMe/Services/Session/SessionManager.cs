using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using InTheHand.Net;

namespace DropMe.Services.Session;

public class SessionManager : IDisposable {
    public enum TransferDirection {
        Send, Receive
    }

    public sealed record TransferLogInfo(
        Guid FileId,
        string FilePath,
        TransferDirection Direction,
        DateTime CompletedAt,
        long FileSizeBytes,
        bool Successful);

    public Func<FileOfferInfo, Task<bool>>? FileOfferDecision { get; set; }
    public bool TcpConnected => _connectionManager.TcpConnected;
    public bool BtConnected => _connectionManager.BluetoothConnected;
    public bool IsConnected => _connectionManager.IsConnected;
    public string? PeerName => _connectionManager.PeerName;
    public event Action<string>? FileSaved;
    public event Action<Guid, string /*sha256 hex*/>? FileAcked;
    public event Action<string>? PeerNameUpdated;
    public event Action<ConnectionEndReason>? SessionEnded;
    public List<TransferLogInfo> CompletedTransfers { get; } = [];

    public byte[]? AesSessionKey {
        set => _connectionManager.AesSessionKey = value;
    } // MUST BE SET BEFORE A CONNECTION IS ESTABLISHED

    private readonly CancellationTokenSource _sessionCtSource = new();
    private readonly ConnectionManager _connectionManager;
    private readonly IStorageService _storageService;
    private readonly Dictionary<Guid, FileTransferState> _fileTransferStates = new Dictionary<Guid, FileTransferState>();
    private bool _disposed = false;

    public SessionManager(IStorageService storageService) {
        _storageService = storageService;
        _connectionManager = new ConnectionManager(_sessionCtSource.Token);
        _connectionManager.ConnectionEnded += OnSessionEnded;
        _connectionManager.PeerNameUpdated += name => PeerNameUpdated?.Invoke(name);
    }

    public Task ListenTcp(IPEndPoint listenEp, CancellationToken ct) {
        return _connectionManager.TryAcceptTcpConnection(listenEp, ct);
    }

    public Task ListenTcpAndBt(IPEndPoint listenEp, CancellationToken ct) {
        return _connectionManager.TryAcceptTcpAndBtConnections(listenEp, ct);
    }

    public Task EstablishConnections(IPEndPoint? lanServerEp, BluetoothAddress? btAddr, string? btName, CancellationToken ct) {
        Console.WriteLine($"Trying to establish connection with {lanServerEp} {btAddr} {btName}");
        return _connectionManager.EstablishConnections(lanServerEp, btAddr, btName, ct);
    }

    public async Task StartReceiveLoop() {
        Debug.Assert(!_disposed, "Cannot receive on disposed session, create a new session manager");
        await foreach (var msg in _connectionManager.ReceiveMessages()) {
            await RespondToDataMessage(msg);
        }
        Console.WriteLine("Exiting receive loop");
    }

    public Task AnnounceDeviceName(string name, CancellationToken ct) {
        if (string.IsNullOrWhiteSpace(name))
            return Task.CompletedTask;

        return _connectionManager.SendMessage(new DeviceNameMsg(name), ct);
    }

    /// <summary>
    /// Stops and disposes this session manager.
    /// </summary>
    public async Task StopSession(bool notifyPeer = true) {
        Console.WriteLine("Stop session called");
        // Try to notify peer that a disconnection has been requested
        if (notifyPeer && _connectionManager.IsConnected)
            await _connectionManager.SendMessage(new DisconnectMsg(), CancellationToken.None);

        await _sessionCtSource.CancelAsync();
        Dispose();
    }

    // There would be race conditions if multiple threads try to modify the same _fileTransferStates[fileId]
    // This should be fine as after it is SendInProgress, this thread is the only one modifying that fileId
    public async Task SendFileAsync(Stream file, string filename, CancellationToken ct) {
        Debug.Assert(!_disposed, "Cannot send on disposed session, create a new session manager");
        var fileLength = file.Length;
        var fileId = Guid.NewGuid();
        var offer = new FileOfferInfo(fileId, filename, fileLength);
        await _connectionManager.SendMessage(new FileOfferMsg(fileId, filename, fileLength), ct).ConfigureAwait(false);
        var tcs = new TaskCompletionSource<bool>();
        lock (_fileTransferStates) {
            _fileTransferStates.Add(fileId, new AwaitingDecision(offer, tcs));
        }
        Console.WriteLine("Awaiting acceptance");
        var fileAccepted = await tcs.Task;
        Console.WriteLine("File accepted");

        if (!fileAccepted)
            throw new InvalidOperationException("Peer rejected the file.");

        var fileState = new SendInProgress(file, IncrementalHash.CreateHash(HashAlgorithmName.SHA256), 0);
        lock (_fileTransferStates) {
            _fileTransferStates[fileId] = fileState;
        }

        const int chunkSize = 8 * 1024;
        var buffer = new byte[chunkSize];
        var chunkIndex = 0;

        while (true) {
            var n = await file.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (n <= 0) break;

            fileState.Hash.AppendData(buffer, 0, n);
            try {
                await _connectionManager.SendMessage(new FileChunkMsg(fileId, chunkIndex++, buffer[0..n]), ct)
                    .ConfigureAwait(false);

            }
            catch (Exception e) {
                Console.WriteLine($"Exception sending file data message {e}");
            }
        }

        var hash = fileState.Hash.GetHashAndReset(); // 32 bytes
        lock (_fileTransferStates) {
            _fileTransferStates[fileId] = new AwaitingAck(filename, fileLength, hash);
        }

        Debug.Assert(hash.Length == 32, "Sha256 hash should be 32 bytes");
        await _connectionManager.SendMessage(new FileDoneMsg(fileId, hash), ct).ConfigureAwait(false);
    }

    private async Task RespondToDataMessage(FileMsg message) {
        Console.WriteLine($"Responding to data message {message}");
        switch (message) {
            case FileOfferMsg(var fileId, var fileName, var fileSize):
                Console.WriteLine($"File {fileId}  {fileName} offered");
                var accept = FileOfferDecision is null || await FileOfferDecision(new FileOfferInfo(fileId, fileName, fileSize)).ConfigureAwait(false);
                if (accept) {
                    bool accepted;
                    lock (_fileTransferStates) {
                        var output =
                            _storageService.OpenDownloadFileWriteStream(
                                $"recv_{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizeName(fileName)}");
                        if (output is var (stream, outputPath)) {
                            var ableToAdd = _fileTransferStates.TryAdd(fileId,
                                new ReceiveInProgress(stream, outputPath, fileSize, 0,
                                    IncrementalHash.CreateHash(HashAlgorithmName.SHA256), 0));
                            accepted = ableToAdd;
                        }
                        else {
                            accepted = false;
                        }
                    }

                    if (accepted) {
                        await _connectionManager.SendMessage(new FileAcceptMsg(fileId), _sessionCtSource.Token).ConfigureAwait(false);
                    }
                    else {
                        await _connectionManager.SendMessage(new FileRejectMsg(fileId, FileRejectReason.InternalError), _sessionCtSource.Token).ConfigureAwait(false);
                    }
                }
                else {
                    await _connectionManager.SendMessage(new FileRejectMsg(fileId, FileRejectReason.UserRejected), _sessionCtSource.Token).ConfigureAwait(false);
                }
                break;
            case FileAcceptMsg(var fileId):
                lock (_fileTransferStates) {
                    _fileTransferStates.TryGetValue(fileId, out var fileAcceptTs);
                    if (fileAcceptTs is AwaitingDecision decisionTs) {
                        // Does not update the transfer states data structure, must do this where the file
                        // sending is occuring as needs the stream and hash

                        decisionTs.DecisionTcs.TrySetResult(true);
                    }
                }
                break;
            case FileRejectMsg(var fileId, var reason):
                lock (_fileTransferStates) {
                    _fileTransferStates.TryGetValue(fileId, out var fileRejectTs);
                    if (fileRejectTs is AwaitingDecision decisionTs) {
                        _fileTransferStates.Remove(fileId);
                        decisionTs.DecisionTcs.TrySetResult(false);
                    }
                    else if (fileRejectTs is AwaitingAck) {
                        // Just remove the file if it has been rejected for now
                        _fileTransferStates.Remove(fileId);
                    }
                }
                break;
            case FileChunkMsg(var fileId, var chunkIndex, var data):
                FileTransferState? fileTs;
                lock (_fileTransferStates) {
                    _fileTransferStates.TryGetValue(fileId, out fileTs);
                }

                if (fileTs is ReceiveInProgress recvState) {
                    if (chunkIndex != recvState.ExpectedChunkIndex) return;
                    await recvState.SaveStream.WriteAsync(data, _sessionCtSource.Token).ConfigureAwait(false);
                    recvState.ExpectedChunkIndex++;
                    recvState.WrittenBytes += data.Length;
                    recvState.Hash.AppendData(data.Span);
                }
                break;
            case FileDoneMsg(var fileId, var sha256):
                await HandleFileDone(fileId, sha256);
                break;
            case FileAckMsg(var fileId, var _):
                lock (_fileTransferStates) {
                    _fileTransferStates.TryGetValue(fileId, out var fileAckState);
                    // Only remove if it is actually awaiting an ack
                    if (fileAckState is AwaitingAck awaitingAck) {
                        _fileTransferStates.Remove(fileId);
                        CompletedTransfers.Add(new TransferLogInfo(fileId, awaitingAck.FilePath, TransferDirection.Send, DateTime.Now, awaitingAck.FileSizeBytes, true));
                        FileAcked?.Invoke(fileId, awaitingAck.Hash.ToString()!);
                    }
                }
                break;
            default: throw new Exception("Unknown message");
        }
    }

    private async Task HandleFileDone(Guid fileId, ReadOnlyMemory<byte> sha256) {
        FileTransferState? fileDoneTs;
        lock (_fileTransferStates) {
            _fileTransferStates.TryGetValue(fileId, out fileDoneTs);
        }

        if (fileDoneTs is ReceiveInProgress completedState) {
            await completedState.SaveStream.FlushAsync().ConfigureAwait(false);
            await completedState.SaveStream.DisposeAsync().ConfigureAwait(false);
            var localHash = completedState.Hash.GetHashAndReset();
            completedState.Hash.Dispose();

            var successful = true;
            if (completedState.WrittenBytes != completedState.ExpectedSizeBytes) {
                await _connectionManager.SendMessage(new FileRejectMsg(fileId, FileRejectReason.SizeMismatch), _sessionCtSource.Token).ConfigureAwait(false);
                successful = false;
                // DELETE FILE PROPERLY
                return;
            }

            if (!CryptographicOperations.FixedTimeEquals(localHash, sha256.Span)) {
                await _connectionManager.SendMessage(new FileRejectMsg(fileId, FileRejectReason.HashMismatch), _sessionCtSource.Token).ConfigureAwait(false);
                successful = false;
                //Delete file properly
                return;
            }

            var savedInfo = new TransferLogInfo(fileId, completedState.SavePath, TransferDirection.Receive, DateTime.Now, completedState.ExpectedSizeBytes, successful);
            CompletedTransfers.Add(savedInfo);
            Console.WriteLine($"Completed {CompletedTransfers[0]}");
            FileSaved?.Invoke(completedState.SavePath);
            await _connectionManager.SendMessage(new FileAckMsg(fileId, sha256), _sessionCtSource.Token).ConfigureAwait(false);
        }
    }

    private static string SanitizeName(string name) {
        return Path.GetInvalidFileNameChars().Aggregate(name, (current, c) => current.Replace(c, '_'));
    }

    private void OnSessionEnded(ConnectionEndReason reason) {
        Console.WriteLine("Session ended");
        SessionEnded?.Invoke(reason);
    }

    public void Dispose() {
        _disposed = true;
        _connectionManager.Dispose();
    }
}
