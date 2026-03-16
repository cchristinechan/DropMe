using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace DropMe.Services.Session;

public class BluetoothServerSession(IBluetoothListener listener) : ISession {
    public SessionState State { get; }
    public string Peer { get; }
    public event Action<string>? FileSaved;
    public event Action<Guid, string>? FileAcked;
    private Func<FileOfferInfo, Task<bool>>? _fileOfferDecision;
    private AesGcmFileTransfer<Stream>? _transferService;
    public Func<FileOfferInfo, Task<bool>>? FileOfferDecision {
        get => _fileOfferDecision;
        set {
            _fileOfferDecision = value;
            _transferService?.FileOfferDecision = value;
        }
    }

    public async Task Connect(CancellationToken ct) {
        Console.WriteLine("Starting listener " + listener.GetType());



        BluetoothRadio radio = BluetoothRadio.Default;
        radio.Mode = RadioMode.Discoverable;
        listener.Start();
        Console.WriteLine("Bluetooth server started, waiting for connections...");

        BluetoothClient client = await listener.AcceptBluetoothClientAsync();
        Console.WriteLine("Client connected!");
        radio.Mode = RadioMode.Connectable;

        var stream = client.GetStream();
        throw new NotImplementedException();
    }

    public Task StartAsync(CancellationToken ct) {
        throw new System.NotImplementedException();
    }

    public Task SendFileAsync(Stream file, string filename, CancellationToken ct) {
        throw new System.NotImplementedException();
    }

    public Task StopAsync() {
        throw new System.NotImplementedException();
    }
}