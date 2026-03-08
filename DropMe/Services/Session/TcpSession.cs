using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services.Session;

public sealed class TcpHostSession(IStorageService storageService, IPEndPoint listenEp) : ISession {
    private TcpListener? _listener;
    private AesGcmFileTransfer<NetworkStream>? _transferService;

    private Func<AesGcmFileTransfer<NetworkStream>.FileOfferInfo, Task<bool>>? _fileOfferDecision;

    public event Action<string>? FileSaved;
    public event Action<Guid, string /*sha256 hex*/>? FileAcked;

    public Func<AesGcmFileTransfer<NetworkStream>.FileOfferInfo, Task<bool>>? FileOfferDecision {
        get => _fileOfferDecision;
        set {
            _fileOfferDecision = value;
            _transferService?.FileOfferDecision = value;
        }
    }

    public SessionState State { get; private set; } = SessionState.Idle;
    public string Peer => _transferService?.PeerName ?? "waiting…";

    public async Task StartAsync(CancellationToken ct) {
        State =  SessionState.Idle;
        _listener = new TcpListener(listenEp);
        _listener.Start();

        using var client = await _listener.AcceptTcpClientAsync(ct);
        var ep = (IPEndPoint?)client.Client.RemoteEndPoint ?? listenEp;
        
        _transferService = new AesGcmFileTransfer<NetworkStream>(client.GetStream(), ep.ToString(), storageService);
        _transferService.FileSaved += path => FileSaved?.Invoke(path);
        _transferService.FileAcked += (id, sha) => FileAcked?.Invoke(id, sha);
        _transferService.FileOfferDecision = _fileOfferDecision;
        try {
            await _transferService.Start(ct).ConfigureAwait(false);
            State = SessionState.Connected;
        }
        catch (OperationCanceledException e) {

        }
        catch (Exception e) {
            State = SessionState.Error;
        }
    }

    public Task SendFileAsync(Stream file, string filename, CancellationToken ct) {
        if (_transferService is null) throw new InvalidOperationException("Not connected.");
        return _transferService.SendFileAsync(file, filename, ct);
    }

    public async Task StopAsync() {
        if (_transferService is not null) {
            State = SessionState.Closed;
            await _transferService.StopAsync();
        }

        _listener?.Stop();
    }
}
