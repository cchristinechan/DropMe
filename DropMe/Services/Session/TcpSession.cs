using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System;

namespace DropMe.Services.Session;


public sealed class TcpSession : ISession {
    private readonly IPEndPoint _endpoint;
    private TcpClient? _client;
    private NetworkStream? _stream;

    public SessionState State { get; private set; } = SessionState.Idle;
    public string Peer => _endpoint.ToString();

    public TcpSession(IPEndPoint endpoint) {
        _endpoint = endpoint;
    }

    public async Task StartAsync(CancellationToken ct) {
        State = SessionState.Connecting;

        _client = new TcpClient();
        await _client.ConnectAsync(_endpoint, ct);
        _stream = _client.GetStream();

        State = SessionState.Connected;

        // 🔑 start background loops AFTER connection
        _ = Task.Run(() => ReceiveLoop(ct), ct);
        _ = Task.Run(() => KeepAliveLoop(ct), ct);
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
        finally {
            State = SessionState.Closed;
        }
    }

    public async Task SendFileAsync(string path, CancellationToken ct) {
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
    private async Task KeepAliveLoop(CancellationToken ct) {
        try {
            while (!ct.IsCancellationRequested) {
                // Send a tiny keepalive frame
                await SendKeepAliveAsync(ct);

                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        catch (OperationCanceledException) {
            // normal shutdown
        }
        catch {
            State = SessionState.Error;
        }
    }
    private async Task SendKeepAliveAsync(CancellationToken ct) {
        if (_stream is null) return;

        // PoC: single byte ping
        await _stream.WriteAsync(new byte[] { 0x00 }, ct);
        await _stream.FlushAsync(ct);
    }



}