using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using DropMe.Services;
namespace DropMe.ViewModels;

public class MainViewModel {
    private readonly IWorkManager _work;
    private readonly IFileTransfer _transfer;

    public MainViewModel(IWorkManager work, IFileTransfer transfer) {
        _work = work;
        _transfer = transfer;
    }

    // Run local PoC for TCP AES-GCM chunked file transfer with header AAD
    public void RunLocalHelloWorldTransfer() {
        _work.ScheduleWork(async () => {
            try {
                // Create hello world to send
                string tempDir = Path.Combine(Path.GetTempPath(), "DropMe-PoC"); // GetTempPath for cross platform
                Directory.CreateDirectory(tempDir);
                string sourcePath = Path.Combine(tempDir, "helloworld.txt");
                await File.WriteAllTextAsync(sourcePath, "Hello world!\n").ConfigureAwait(false);

                // Recieve path
                string destPath = Path.Combine(tempDir, "helloworld.received.txt");

                // Free port on loopback
                int port = GetFreePort();
                var ep = new IPEndPoint(IPAddress.Loopback, port);

                // Shared key for AES-GCM (32 bytes, so AES-256)
                byte[] key = new byte[32];
                RandomNumberGenerator.Fill(key);

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                // Start receiver (listening)
                Task<FileTransferHeader> recvTask =
                    _transfer.ReceiveFileEncryptedAsync(ep, destPath, key, cts.Token);

                // small PoC delay to allow listener to start
                await Task.Delay(75, cts.Token).ConfigureAwait(false);

                // Send file
                await _transfer.SendFileEncryptedAsync(sourcePath, ep, key, chunkSize: 64 * 1024, cts.Token)
                    .ConfigureAwait(false);

                // Wait for completion
                FileTransferHeader header = await recvTask.ConfigureAwait(false);

                // Verify arrival
                string receivedText = await File.ReadAllTextAsync(destPath, cts.Token).ConfigureAwait(false);

                // PoC output: write to debug output
                System.Diagnostics.Debug.WriteLine("=== DropMe PoC Transfer Complete ===");
                System.Diagnostics.Debug.WriteLine($"Sent file: {sourcePath}");
                System.Diagnostics.Debug.WriteLine($"Received file: {destPath}");
                System.Diagnostics.Debug.WriteLine($"Header name: {header.FileName}");
                System.Diagnostics.Debug.WriteLine($"Header size: {header.FileSize}");
                System.Diagnostics.Debug.WriteLine($"Received text: {receivedText.Trim()}");

                // Update UI below
                /*Avalonia.Threading.Dispatcher.UIThread.Post(() => Status = "Transfer complete!");*/
            }
            catch (Exception ex) {
                System.Diagnostics.Debug.WriteLine($"PoC transfer failed: {ex}");
                // Update UI below
                /*Dispatcher.UIThread.Post(() => Status = ex.Message);*/
            }
        });
    }

    private static int GetFreePort() {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }
}
