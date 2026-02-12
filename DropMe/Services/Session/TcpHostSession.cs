using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services.Session;

public sealed class TcpHostSession : ISession
{
    private readonly IPEndPoint _listenEp;
    private TcpListener? _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;

    public SessionState State { get; private set; } = SessionState.Idle;
    public string Peer { get; private set; } = "waiting…";

    public TcpHostSession(IPEndPoint listenEp)
    {
        _listenEp = listenEp;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        State = SessionState.Connecting;

        _listener = new TcpListener(_listenEp);
        _listener.Start();

        // Wait for a peer
        _client = await _listener.AcceptTcpClientAsync(ct);
        _stream = _client.GetStream();

        Peer = _client.Client.RemoteEndPoint?.ToString() ?? "peer";
        State = SessionState.Connected;

        _ = Task.Run(() => ReceiveLoop(ct), ct);
        _ = Task.Run(() => KeepAliveLoop(ct), ct);
    }

    public async Task SendFileAsync(string path, CancellationToken ct)
    {
        using var fs = System.IO.File.OpenRead(path);
        await fs.CopyToAsync(_stream!, ct);
        await _stream!.FlushAsync(ct);
    }

    public Task StopAsync()
    {
        State = SessionState.Closed;
        _stream?.Dispose();
        _client?.Dispose();
        _listener?.Stop();
        return Task.CompletedTask;
    }

    private async Task ReceiveLoop(CancellationToken ct)
    {
        var buf = new byte[1];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                int n = await _stream!.ReadAsync(buf, ct);
                if (n == 0) break;
            }
        }
        catch { State = SessionState.Error; }
        finally { State = SessionState.Closed; }
    }

    private async Task KeepAliveLoop(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await _stream!.WriteAsync(new byte[] { 0x00 }, ct);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
        catch { State = SessionState.Error; }
    }
}
