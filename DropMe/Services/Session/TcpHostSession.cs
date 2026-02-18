using System;
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
    private string? _downloadDirectory;

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
    public string? DownloadDirectory {
        get => _downloadDirectory;
        set {
            _downloadDirectory = value;
            if (_session is not null)
                _session.DownloadDirectory = value;
        }
    }

    public TcpHostSession(IPEndPoint listenEp) {
        _listenEp = listenEp;
    }

    public async Task StartAsync(CancellationToken ct) {
        _listener = new TcpListener(_listenEp);
        _listener.Start();

        _client = await _listener.AcceptTcpClientAsync(ct);

        var ep = (IPEndPoint?)_client.Client.RemoteEndPoint ?? _listenEp;
        _session = new TcpAesGcmSession(ep);
        _session.DownloadDirectory = _downloadDirectory;
        _session.AttachAcceptedClient(_client);

        _session.FileSaved += path => FileSaved?.Invoke(path);
        _session.FileAcked += (id, sha) => FileAcked?.Invoke(id, sha);
        _session.FileOfferDecision = _fileOfferDecision;

        await _session.StartAsAcceptedAsync(ct);
    }

    public Task SendFileAsync(string path, CancellationToken ct) {
        if (_session is null) throw new InvalidOperationException("Not connected.");
        return _session.SendFileAsync(path, ct);
    }

    public async Task StopAsync() {
        if (_session is not null)
            await _session.StopAsync();

        _client?.Dispose();
        _listener?.Stop();
    }
}
