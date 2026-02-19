using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services.Session;

public sealed class TcpAesGcmSession : ISession {
    private readonly IPEndPoint _endpoint;
    private TcpClient? _client;
    private NetworkStream? _stream;

    private readonly SemaphoreSlim _writeLock = new(1, 1);

    // receive-side file state (PoC: only one file at a time)
    private Stream? _rxStream;
    private Guid _rxFileId;
    private long _rxExpectedSize;
    private long _rxWritten;
    private string? _rxPath;
    private IncrementalHash? _rxHash;
    private int _rxExpectedChunkIndex;

    private readonly object _txLock = new();
    private TaskCompletionSource<bool>? _txOfferTcs;
    private Guid _txOfferFileId;
    private string? _txRejectReason;
    private readonly Dictionary<Guid, byte[]> _txExpectedAckHashes = new();

    public Func<FileOfferInfo, Task<bool>>? FileOfferDecision { get; set; }

    public sealed record FileOfferInfo(Guid FileId, string Name, long Size);

    public event Action<string>? FileSaved;
    public event Action<Guid, string /*sha256 hex*/>? FileAcked;

    public SessionState State { get; private set; } = SessionState.Idle;
    public string Peer => _endpoint.ToString();
    private readonly IStorageService _storageService;

    public TcpAesGcmSession(IStorageService storageService, IPEndPoint endpoint) {
        _endpoint = endpoint;
        _storageService = storageService;
    }

    public void AttachAcceptedClient(TcpClient client) {
        _client = client;
        _stream = client.GetStream();
    }

    public Task StartAsAcceptedAsync(CancellationToken ct) {
        if (_stream is null) throw new InvalidOperationException("No accepted client.");

        State = SessionState.Connected;
        _ = Task.Run(() => ReceiveLoop(ct), ct);
        _ = Task.Run(() => KeepAliveLoop(ct), ct);
        return Task.CompletedTask;
    }

    public async Task StartAsync(CancellationToken ct) {
        State = SessionState.Connecting;

        _client = new TcpClient();
        await _client.ConnectAsync(_endpoint, ct).ConfigureAwait(false);

        _stream = _client.GetStream();
        State = SessionState.Connected;

        _ = Task.Run(() => ReceiveLoop(ct), ct);
        _ = Task.Run(() => KeepAliveLoop(ct), ct);
    }

    public async Task SendFileAsync(Stream file, string filename, CancellationToken ct) {
        if (_stream is null) throw new InvalidOperationException("Not connected.");

        var fileId = Guid.NewGuid();

        var offer = new FileOffer {
            FileId = fileId,
            Name = filename,
            Size = file.Length
        };

        lock (_txLock) {
            _txOfferFileId = fileId;
            _txRejectReason = null;
            _txOfferTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // Offer
        await SendMessageAsync(SessionMessageType.FileOffer, JsonSerializer.SerializeToUtf8Bytes(offer), ct).ConfigureAwait(false);

        bool accepted;
        try {
            accepted = await WaitForOfferDecisionAsync(fileId, ct).ConfigureAwait(false);
        }
        finally {
            lock (_txLock) _txOfferTcs = null;
        }

        if (!accepted)
            throw new InvalidOperationException(_txRejectReason ?? "Peer rejected the file.");

        // Stream chunks + compute hash while sending
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        const int chunkSize = 64 * 1024;
        var buffer = new byte[chunkSize];
        int chunkIndex = 0;

        while (true) {
            int n = await file.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (n <= 0) break;

            sha.AppendData(buffer, 0, n);

            var payload = BuildFileChunkPayload(fileId, chunkIndex++, buffer.AsSpan(0, n));
            await SendMessageAsync(SessionMessageType.FileChunk, payload, ct).ConfigureAwait(false);
        }

        var hash = sha.GetHashAndReset(); // 32 bytes
        lock (_txLock) {
            _txExpectedAckHashes[fileId] = (byte[])hash.Clone();
        }

        var endPayload = BuildFileEndPayload(fileId, hash);
        await SendMessageAsync(SessionMessageType.FileDone, endPayload, ct).ConfigureAwait(false);
    }

    public Task StopAsync() {
        State = SessionState.Closed;
        try { _rxStream?.Dispose(); } catch { }
        try { _rxHash?.Dispose(); } catch { }
        lock (_txLock) {
            _txExpectedAckHashes.Clear();
        }
        _stream?.Dispose();
        _client?.Dispose();
        return Task.CompletedTask;
    }

    private async Task ReceiveLoop(CancellationToken ct) {
        try {
            while (!ct.IsCancellationRequested && _stream is not null) {
                var (type, payload) = await SessionFraming.ReadAsync(_stream, ct).ConfigureAwait(false);

                switch (type) {
                    case SessionMessageType.Ping:
                        await SendMessageAsync(SessionMessageType.Pong, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
                        break;

                    case SessionMessageType.Pong:
                        // optionally update last-seen timestamp
                        break;

                    case SessionMessageType.FileOffer:
                        await HandleFileOfferAsync(payload, ct).ConfigureAwait(false);
                        break;

                    case SessionMessageType.FileAccept:
                        HandleFileAccept(payload);
                        break;

                    case SessionMessageType.FileReject:
                        HandleFileReject(payload);
                        break;

                    case SessionMessageType.FileChunk:
                        await HandleFileChunkAsync(payload, ct).ConfigureAwait(false);
                        break;

                    case SessionMessageType.FileDone:
                        await HandleFileEndAsync(payload, ct).ConfigureAwait(false);
                        break;

                    case SessionMessageType.FileAck:
                        HandleFileAck(payload);
                        break;

                    default:
                        // unknown message: ignore for PoC
                        break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception) {
            State = SessionState.Error;
        }
        finally {
            State = SessionState.Closed;
        }
    }

    private async Task KeepAliveLoop(CancellationToken ct) {
        try {
            while (!ct.IsCancellationRequested && _stream is not null) {
                await SendMessageAsync(SessionMessageType.Ping, ReadOnlyMemory<byte>.Empty, ct).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(2), ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch {
            State = SessionState.Error;
        }
    }

    private async Task SendMessageAsync(SessionMessageType type, ReadOnlyMemory<byte> payload, CancellationToken ct) {
        if (_stream is null) throw new InvalidOperationException("Not connected.");

        await _writeLock.WaitAsync(ct).ConfigureAwait(false);
        try {
            await SessionFraming.WriteAsync(_stream, type, payload, ct).ConfigureAwait(false);
        }
        finally {
            _writeLock.Release();
        }
    }

    // ---------------- Receive handlers ----------------

    private async Task HandleFileOfferAsync(byte[] payload, CancellationToken ct) {
        var offer = JsonSerializer.Deserialize<FileOffer>(payload);
        if (offer is null) return;

        var info = new FileOfferInfo(offer.FileId, offer.Name, offer.Size);
        bool accept = FileOfferDecision is not null
            ? await FileOfferDecision(info).ConfigureAwait(false)
            : true;

        if (!accept) {
            var rej = new FileReject { FileId = offer.FileId, Reason = "User rejected" };
            await SendMessageAsync(SessionMessageType.FileReject, JsonSerializer.SerializeToUtf8Bytes(rej), ct).ConfigureAwait(false);
            return;
        }

        // PoC: overwrite any current receive
        _rxStream?.Dispose();
        _rxHash?.Dispose();

        _rxFileId = offer.FileId;
        _rxExpectedSize = offer.Size;
        _rxWritten = 0;
        _rxExpectedChunkIndex = 0;

        //var dir = string.IsNullOrWhiteSpace(DownloadDirectory)
        //    ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DropMeReceived")
        //    : DownloadDirectory;
        //Directory.CreateDirectory(dir);
        var output =
            _storageService.OpenDownloadFileWriteStreamAsync(
                $"recv_{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizeName(offer.Name)}");

        if (output is var (stream, path)) {
            _rxStream = stream;
            _rxPath = path;
        }

        _rxHash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        await SendMessageAsync(SessionMessageType.FileAccept, BuildFileIdPayload(offer.FileId), ct).ConfigureAwait(false);
    }

    private async Task HandleFileChunkAsync(byte[] payload, CancellationToken ct) {
        if (_rxStream is null || _rxHash is null) return;

        if (!TryParseFileChunkPayload(payload, out var fileId, out var chunkIndex, out var data))
            return;

        if (fileId != _rxFileId) return; // PoC: only one file at a time

        if (chunkIndex != _rxExpectedChunkIndex)
            return;

        _rxExpectedChunkIndex++;

        await _rxStream.WriteAsync(data, ct).ConfigureAwait(false);
        _rxHash.AppendData(data.Span);
        _rxWritten += data.Length;
    }

    private async Task HandleFileEndAsync(byte[] payload, CancellationToken ct) {
        if (_rxStream is null || _rxHash is null || _rxPath is null) return;

        if (!TryParseFileEndPayload(payload, out var fileId, out var senderHash))
            return;

        if (fileId != _rxFileId) return;

        await _rxStream.FlushAsync(ct).ConfigureAwait(false);
        await _rxStream.DisposeAsync();
        _rxStream = null;

        var localHash = _rxHash.GetHashAndReset();
        _rxHash.Dispose();
        _rxHash = null;

        if (_rxWritten != _rxExpectedSize) {
            await SendTransferRejectAsync(fileId, "Size mismatch", ct).ConfigureAwait(false);
            TryDeleteFile(_rxPath);
            ResetReceiveState();
            return;
        }

        if (!CryptographicOperations.FixedTimeEquals(localHash, senderHash)) {
            await SendTransferRejectAsync(fileId, "SHA-256 mismatch", ct).ConfigureAwait(false);
            TryDeleteFile(_rxPath);
            ResetReceiveState();
            return;
        }

        // Notify UI where file went only when integrity checks pass.
        FileSaved?.Invoke(_rxPath);

        // ACK back with our computed hash (must match sender hash at this point).
        var ackPayload = BuildFileAckPayload(fileId, localHash);
        await SendMessageAsync(SessionMessageType.FileAck, ackPayload, ct).ConfigureAwait(false);

        ResetReceiveState();
    }

    private void HandleFileAck(byte[] payload) {
        if (!TryParseFileAckPayload(payload, out var fileId, out var hash))
            return;

        byte[]? expectedHash = null;
        lock (_txLock) {
            if (_txExpectedAckHashes.TryGetValue(fileId, out var h))
                expectedHash = h;
        }

        if (expectedHash is null)
            return;

        if (!CryptographicOperations.FixedTimeEquals(expectedHash, hash))
            return;

        lock (_txLock) {
            _txExpectedAckHashes.Remove(fileId);
        }

        FileAcked?.Invoke(fileId, Convert.ToHexString(hash));
    }

    private void HandleFileAccept(byte[] payload) {
        if (!TryParseFileIdPayload(payload, out var id)) return;

        lock (_txLock) {
            if (_txOfferTcs is null || id != _txOfferFileId) return;
            _txOfferTcs.TrySetResult(true);
        }
    }

    private void HandleFileReject(byte[] payload) {
        FileReject? rej = null;
        try { rej = JsonSerializer.Deserialize<FileReject>(payload); } catch { }

        if (rej is null) return;

        lock (_txLock) {
            if (_txOfferTcs is null || rej.FileId != _txOfferFileId) return;
            _txRejectReason = rej.Reason;
            _txOfferTcs.TrySetResult(false);
        }
    }

    private async Task<bool> WaitForOfferDecisionAsync(Guid fileId, CancellationToken ct) {
        TaskCompletionSource<bool>? tcs;
        lock (_txLock) tcs = _txOfferTcs;

        if (tcs is null) throw new InvalidOperationException("No offer is pending.");

        using var reg = ct.Register(() => tcs.TrySetCanceled(ct));
        return await tcs.Task.ConfigureAwait(false);
    }

    private async Task SendTransferRejectAsync(Guid fileId, string reason, CancellationToken ct) {
        var rej = new FileReject { FileId = fileId, Reason = reason };
        await SendMessageAsync(SessionMessageType.FileReject, JsonSerializer.SerializeToUtf8Bytes(rej), ct).ConfigureAwait(false);
    }

    private static void TryDeleteFile(string? path) {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch {
            // best effort cleanup
        }
    }

    private void ResetReceiveState() {
        _rxPath = null;
        _rxExpectedSize = 0;
        _rxWritten = 0;
        _rxExpectedChunkIndex = 0;
    }

    // ---------------- Payload formats ----------------
    // Chunk: [fileId(16)][chunkIndex(4 LE)][data...]
    private static byte[] BuildFileChunkPayload(Guid fileId, int chunkIndex, ReadOnlySpan<byte> data) {
        var buf = new byte[16 + 4 + data.Length];
        fileId.TryWriteBytes(buf.AsSpan(0, 16));
        BinaryPrimitives.WriteInt32LittleEndian(buf.AsSpan(16, 4), chunkIndex);
        data.CopyTo(buf.AsSpan(20));
        return buf;
    }

    private static bool TryParseFileChunkPayload(byte[] payload, out Guid fileId, out int chunkIndex, out ReadOnlyMemory<byte> data) {
        fileId = default;
        chunkIndex = 0;
        data = default;

        if (payload.Length < 20) return false;

        fileId = new Guid(payload.AsSpan(0, 16));
        chunkIndex = BinaryPrimitives.ReadInt32LittleEndian(payload.AsSpan(16, 4));
        data = payload.AsMemory(20);
        return true;
    }

    // End: [fileId(16)][sha256(32)]
    private static byte[] BuildFileEndPayload(Guid fileId, ReadOnlySpan<byte> sha256) {
        if (sha256.Length != 32) throw new ArgumentException("SHA-256 must be 32 bytes.");
        var buf = new byte[16 + 32];
        fileId.TryWriteBytes(buf.AsSpan(0, 16));
        sha256.CopyTo(buf.AsSpan(16));
        return buf;
    }

    private static bool TryParseFileEndPayload(byte[] payload, out Guid fileId, out byte[] sha256) {
        fileId = default;
        sha256 = Array.Empty<byte>();

        if (payload.Length != 48) return false;

        fileId = new Guid(payload.AsSpan(0, 16));
        sha256 = payload.AsSpan(16, 32).ToArray();
        return true;
    }

    // Ack: [fileId(16)][sha256(32)]
    private static byte[] BuildFileAckPayload(Guid fileId, ReadOnlySpan<byte> sha256) {
        if (sha256.Length != 32) throw new ArgumentException("SHA-256 must be 32 bytes.");
        var buf = new byte[48];
        fileId.TryWriteBytes(buf.AsSpan(0, 16));
        sha256.CopyTo(buf.AsSpan(16));
        return buf;
    }

    private static bool TryParseFileAckPayload(byte[] payload, out Guid fileId, out byte[] sha256) {
        fileId = default;
        sha256 = Array.Empty<byte>();
        if (payload.Length != 48) return false;
        fileId = new Guid(payload.AsSpan(0, 16));
        sha256 = payload.AsSpan(16, 32).ToArray();
        return true;
    }

    private static byte[] BuildFileIdPayload(Guid id) {
        var buf = new byte[16];
        id.TryWriteBytes(buf);
        return buf;
    }

    private static bool TryParseFileIdPayload(byte[] payload, out Guid id) {
        id = default;
        if (payload.Length != 16) return false;
        id = new Guid(payload.AsSpan(0, 16));
        return true;
    }

    private static string SanitizeName(string name) {
        foreach (var c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name;
    }

    private sealed class FileOffer {
        public Guid FileId { get; set; }
        public string Name { get; set; } = "file.bin";
        public long Size { get; set; }
    }

    private sealed class FileReject {
        public Guid FileId { get; set; }
        public string Reason { get; set; } = "Rejected";
    }
}

