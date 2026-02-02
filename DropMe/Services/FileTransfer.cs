using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
namespace DropMe.Services;

public sealed class FileTransfer {
    // Sync PoC
    public void SendFile(string sourceFile, IPEndPoint dest) {
        using var fileStream = File.OpenRead(sourceFile);
        using var client = new TcpClient();
        client.Connect(dest);
        using var networkStream = client.GetStream();
        StreamTransfer.SendStream(fileStream, networkStream);
        // Closing the client sends EOF
    }

    // Listen for incoming TCP connection, receive bytes until sender closes, and write to destFile
    public void RecvFile(IPEndPoint listenEndPoint, string destFile) {
        var listener = new TcpListener(listenEndPoint);
        listener.Start();
        try {
            using var client = listener.AcceptTcpClient();
            using var networkStream = client.GetStream();
            using var fileStream = File.Create(destFile);
            StreamTransfer.RecvStream(networkStream, fileStream);
        }
        finally {
            listener.Stop();
        }
    }

    // Async version
    public async Task SendFileAsync(string sourceFile, IPEndPoint dest, CancellationToken ct = default) {
        await using var fileStream = File.OpenRead(sourceFile);
        using var client = new TcpClient();
        await client.ConnectAsync(dest.Address, dest.Port, ct).ConfigureAwait(false);
        await using var networkStream = client.GetStream();
        await StreamTransfer.SendStreamAsync(fileStream, networkStream, ct).ConfigureAwait(false);
    }

    public async Task RecvFileAsync(IPEndPoint listenEndPoint, string destFile, CancellationToken ct = default) {
        var listener = new TcpListener(listenEndPoint);
        listener.Start();
        try {
            using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            await using var networkStream = client.GetStream();
            await using var fileStream = File.Create(destFile);
            await StreamTransfer.RecvStreamAsync(networkStream, fileStream, ct).ConfigureAwait(false);
        }
        finally {
            listener.Stop();
        }
    }
}
