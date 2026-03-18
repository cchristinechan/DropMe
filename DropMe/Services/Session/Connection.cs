using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using InTheHand.Net.Sockets;

namespace DropMe.Services.Session;

public interface INetworkStreamProvider {
    public NetworkStream NStream { get; }
}

public class TcpClientNsAdapter(TcpClient client) : INetworkStreamProvider, IDisposable {
    public NetworkStream NStream => client.GetStream();

    public void Dispose() {
        client.Dispose();
    }
}

public class BluetoothClientNsAdapter(BluetoothClient client) : INetworkStreamProvider, IDisposable {
    public NetworkStream NStream => client.GetStream();

    public void Dispose() {
        client.Dispose();
    }
}

public interface IConnection : IDisposable {
    public bool IsConnected { get; }
    public string PeerName { get; }

    public Task StartHandlingMessages(ChannelWriter<(ChannelToken, DropMeMsg)> channel, ChannelToken token,
        CancellationToken ct);
    public Task SendMessageAsync(DropMeMsg msg, CancellationToken ct);
}

public class Connection<T>(T streamProvider, string peerName) : IConnection where T : INetworkStreamProvider, IDisposable {
    public bool IsConnected => streamProvider.NStream is { Socket.Connected: true };
    public string PeerName => peerName;
    // It's fine for there to be a reader and a writer thread, but there could potentially be two writers
    // so need to lock on writes
    private readonly SemaphoreSlim _streamWriteSemaphore = new(1, 1);
    private const int PingIntervalSeconds = 2;

    public async Task StartHandlingMessages(ChannelWriter<(ChannelToken, DropMeMsg)> channel, ChannelToken token, CancellationToken ct) {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var kaLoop = Task.Run(() => KeepAliveLoop(ct), cts.Token);
        while (true) {
            try {
                var message = await MessageFraming.ReadAsync(streamProvider.NStream, ct).ConfigureAwait(false);
                await channel.WriteAsync((token, message), ct).ConfigureAwait(false);
            }
            catch (InvalidDataException e) {
                // Allow session to continue with the message dropped if the message is invalid
                Console.WriteLine(e.Message);
            }
            catch (InvalidOperationException e) {
                // The channel has been closed and the caller's no longer accepting messages
                // so quit after cancelling the keep alive loop
                await cts.CancelAsync().ConfigureAwait(false);
                await kaLoop.ConfigureAwait(false);
                return;
            }
            catch (Exception e) {
                Console.WriteLine($"Connection read exception: {e.Message}");
            }
        }
    }

    public async Task SendMessageAsync(DropMeMsg msg, CancellationToken ct) {
        await _streamWriteSemaphore.WaitAsync(ct);
        try {
            await MessageFraming.WriteAsync(streamProvider.NStream, msg, ct);
        }
        catch (Exception e) {
            Console.WriteLine($"Connection write exception: {e.Message}");
        }
        finally {
            _streamWriteSemaphore.Release();
        }
    }

    private async Task KeepAliveLoop(CancellationToken ct) {
        while (!ct.IsCancellationRequested) {
            await SendMessageAsync(new PingMsg(), ct);
            await Task.Delay(TimeSpan.FromSeconds(PingIntervalSeconds), ct).ConfigureAwait(false);
        }
    }

    public void Dispose() {
        Console.WriteLine("Connection disposed");
        streamProvider.Dispose();
    }
}