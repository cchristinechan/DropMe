using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using DropMe.Services.Session;

namespace DropMe.Services;

public sealed class TcpClientSession(IStorageService storageService, IPEndPoint serverEp) : ISession {
    private AesGcmFileTransfer<NetworkStream>? _transferService;
    private Func<FileOfferInfo, Task<bool>>? _fileOfferDecision;
    private TcpClient? _client;

    public event Action<string>? FileSaved;
    public event Action<Guid, string /*sha256 hex*/>? FileAcked;

    public Func<FileOfferInfo, Task<bool>>? FileOfferDecision {
        get => _fileOfferDecision;
        set {
            _fileOfferDecision = value;
            _transferService?.FileOfferDecision = value;
        }
    }

    public SessionState State { get; private set; } = SessionState.Idle;
    public string Peer => _transferService?.PeerName ?? "waiting…";

    public async Task Connect(CancellationToken ct) {
        State = SessionState.Connecting;

        _client = new TcpClient();
        await _client.ConnectAsync(serverEp, ct).ConfigureAwait(false);

        State = SessionState.Connected;
    }

    public async Task StartAsync(CancellationToken ct) {
        if (_client is null) throw new InvalidOperationException("Not connected.");
        var ep = (IPEndPoint?)_client.Client.RemoteEndPoint ?? serverEp;
        State = SessionState.Connected;
        _transferService = new AesGcmFileTransfer<NetworkStream>(_client.GetStream(), ep.ToString(), storageService);
        _transferService.FileSaved += path => FileSaved?.Invoke(path);
        _transferService.FileAcked += (id, sha) => FileAcked?.Invoke(id, sha);
        _transferService.FileOfferDecision = _fileOfferDecision;

        await _transferService.Start(ct);
    }

    public Task SendFileAsync(Stream file, string filename, CancellationToken ct) {
        if (_transferService is null) throw new InvalidOperationException("Not connected.");
        return _transferService.SendFileAsync(file, filename, ct);
    }

    public async Task StopAsync() {
        if (_transferService is not null) {
            State = SessionState.Closed;
            try {
                await _transferService.StopAsync();
            }
            catch (ObjectDisposedException) {
                // Logging?
            }
            catch (Exception ex) {
                // Logging?
            }
        }
    }
}