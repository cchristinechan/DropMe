using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DropMe.Services;
using NUnit.Framework;

public class AeadTransferTests {
    private static int GetFreePort() {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Test]
    public async Task SendsAndReceives_AesGcmChunked() {
        IFileTransfer transfer = new TcpAeadFileTransfer();

        int port = GetFreePort();
        var ep = new IPEndPoint(IPAddress.Loopback, port);

        string src = Path.GetTempFileName();
        string dst = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");

        try {
            var data = new byte[300_000];
            RandomNumberGenerator.Fill(data);
            await File.WriteAllBytesAsync(src, data);

            var key = new byte[32];
            RandomNumberGenerator.Fill(key);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

            var recvTask = transfer.ReceiveFileEncryptedAsync(ep, dst, key, cts.Token);
            await Task.Delay(50, cts.Token); // PoC: allow listener to start
            await transfer.SendFileEncryptedAsync(src, ep, key, 64 * 1024, cts.Token);
            var header = await recvTask;
            var received = await File.ReadAllBytesAsync(dst, cts.Token);
            Assert.That(received, Is.EqualTo(data));
            Assert.That(header.FileSize, Is.EqualTo(data.Length));
            Assert.That(header.Encrypted, Is.True);
        }
        finally {
            if (File.Exists(src)) File.Delete(src);
            if (File.Exists(dst)) File.Delete(dst);
        }
    }
}