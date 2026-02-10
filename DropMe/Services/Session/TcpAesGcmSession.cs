using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System;

namespace DropMe.Services.Session;

public sealed class TcpAesGcmSession : ISession {
    private readonly IPEndPoint _endpoint;
    private TcpClient? _client;
    private NetworkStream? _stream;

    public SessionState State { get; private set; } = SessionState.Idle;

    public string Peer => _endpoint.ToString();

    public TcpAesGcmSession(IPEndPoint endpoint) {
        _endpoint = endpoint;
    }

    public async Task StartAsync(CancellationToken ct) {
        State = SessionState.Connecting;

        _client = new TcpClient();
        await _client.ConnectAsync(_endpoint, ct);

        _stream = _client.GetStream();
        State = SessionState.Connected;

        _ = Task.Run(() => ReceiveLoop(ct), ct);
        _ = Task.Run(() => KeepAliveLoop(ct), ct);
    }

    public async Task SendFileAsync(string path, CancellationToken ct) {
        // PoC: will later add AES-GCM framing here
        using var fs = File.OpenRead(path);
        await fs.CopyToAsync(_stream!, ct);
        await _stream!.FlushAsync(ct);
    }

    public Task StopAsync() {
        State = SessionState.Closed;
        _stream?.Dispose();
        _client?.Dispose();
        return Task.CompletedTask;
    }

    private async Task ReceiveLoop(CancellationToken ct) {
        var buffer = new byte[1];
        try {
            while (!ct.IsCancellationRequested) {
                int read = await _stream!.ReadAsync(buffer, ct);
                if (read == 0)
                    break;
            }
        }
        catch {
            State = SessionState.Error;
        }
        finally {
            State = SessionState.Closed;
        }
    }

    private async Task KeepAliveLoop(CancellationToken ct) {
        try {
            while (!ct.IsCancellationRequested) {
                await _stream!.WriteAsync(new byte[] { 0x00 }, ct);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        catch {
            State = SessionState.Error;
        }
    }
}
