using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services.Session;

public sealed class TcpHostSession : ISession {
    private readonly IPEndPoint _listenEp;
    private TcpListener? _listener;
    private TcpClient? _client;
    private TcpAesGcmSession? _session;
    private IStorageService _storageService;

    private Func<TcpAesGcmSession.FileOfferInfo, Task<bool>>? _fileOfferDecision;

    public event Action<string>? FileSaved;
    public event Action<Guid, string /*sha256 hex*/>? FileAcked;

    public Func<TcpAesGcmSession.FileOfferInfo, Task<bool>>? FileOfferDecision {
        get => _fileOfferDecision;
        set {
            _fileOfferDecision = value;
            if (_session is not null)
                _session.FileOfferDecision = value;
        }
    }

    public SessionState State => _session?.State ?? SessionState.Idle;
    public string Peer => _session?.Peer ?? "waiting…";

    public TcpHostSession(IStorageService storageService, IPEndPoint listenEp) {
        _listenEp = listenEp;
        _storageService = storageService;
    }

    public async Task StartAsync(CancellationToken ct) {
        _listener = new TcpListener(_listenEp);
        _listener.Start();

        _client = await _listener.AcceptTcpClientAsync(ct);

        var ep = (IPEndPoint?)_client.Client.RemoteEndPoint ?? _listenEp;
        _session = new TcpAesGcmSession(_storageService, ep);
        _session.AttachAcceptedClient(_client);

        _session.FileSaved += path => FileSaved?.Invoke(path);
        _session.FileAcked += (id, sha) => FileAcked?.Invoke(id, sha);
        _session.FileOfferDecision = _fileOfferDecision;

        await _session.StartAsAcceptedAsync(ct);
    }

    public Task SendFileAsync(Stream file, string filename, CancellationToken ct) {
        if (_session is null) throw new InvalidOperationException("Not connected.");
        return _session.SendFileAsync(file, filename, ct);
    }

    public async Task StopAsync() {
        if (_session is not null)
            await _session.StopAsync();

        _client?.Dispose();
        _listener?.Stop();
    }
}
