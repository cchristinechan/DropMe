using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace DropMe.Services.Session;

public class BluetoothClientSession : ISession {
    public SessionState State { get; }
    public string Peer { get; }
    public event Action<string>? FileSaved;
    public event Action<Guid, string>? FileAcked;
    private Func<FileOfferInfo, Task<bool>>? _fileOfferDecision;
    private AesGcmFileTransfer<Stream>? _transferService;
    private const string DropMeGuid = "bc8659c9-3aa7-4faf-ba42-c5feb93d1a3e";
    public Func<FileOfferInfo, Task<bool>>? FileOfferDecision {
        get => _fileOfferDecision;
        set {
            _fileOfferDecision = value;
            _transferService?.FileOfferDecision = value;
        }
    }

    public async Task Connect(BluetoothAddress knownAddress, CancellationToken ct) {
        using var client = new BluetoothClient();
        client.Encrypt = true;
        client.Authenticate = true;
        Console.WriteLine($"Attempting to pair with known address {knownAddress}");
        var success = await client.PairAsync(knownAddress).ConfigureAwait(false);
        Console.WriteLine($"Pairing: {success}");
        Console.WriteLine($"Attempting to connect to known address {knownAddress}");
        await client.ConnectAsync(knownAddress, new Guid(DropMeGuid)).ConfigureAwait(false);
        Console.WriteLine($"Connected to {knownAddress}");
        client.GetStream().Write(System.Text.Encoding.UTF8.GetBytes("OK"));
        Console.WriteLine($"Written to {knownAddress}");
    }
    
    public async Task Connect(CancellationToken ct) {
        var serviceGuid = new Guid(DropMeGuid);
        using var client = new BluetoothClient();
        client.Encrypt = true;
        client.Authenticate = true;
        Console.WriteLine("Discovering nearby Bluetooth devices...");
        BluetoothDeviceInfo? matchedDevice = null;
        // Check each device for the target service
        var devices = client.DiscoverDevicesAsync().ConfigureAwait(false);
        var devicesToQuery = new List<BluetoothDeviceInfo>();
        await foreach (var device in devices) {
            device.Refresh();
            Console.WriteLine($"Device: {device.DeviceAddress}");
            if (!string.IsNullOrEmpty(device.DeviceName)) {
                Console.WriteLine($"Device has name {device.DeviceName}");
                devicesToQuery.Add(device);
            }
        }
        Console.WriteLine("Entered querying phase");
        foreach (var device in devicesToQuery) {
            try {
                Console.WriteLine($"Checking device [{device.DeviceAddress}] [{device.DeviceName}] for DropMe");
                if ((await device.GetRfcommServicesAsync(false).ConfigureAwait(false)).Any(s => s == serviceGuid)) {
                    Console.WriteLine($"Found DropMe service on {device.DeviceName}!");
                    matchedDevice = device;
                    break;
                }
            }
            catch (Exception e) {
                Console.WriteLine($"Exception {e.Message}");
            }
        }
        Console.WriteLine("Exited querying phase");

        if (matchedDevice != null) {
            Console.WriteLine("Attempting to connect to the device");
            try {
                var success = await client.PairAsync(matchedDevice.DeviceAddress).ConfigureAwait(false);
                Console.WriteLine($"Pairing: {success}");
                await client.ConnectAsync(matchedDevice.DeviceAddress, serviceGuid).ConfigureAwait(false);
                Console.WriteLine($"Connected to {matchedDevice.DeviceAddress}");
                client.GetStream().Write(System.Text.Encoding.UTF8.GetBytes("OK"));
                Console.WriteLine($"Written to {matchedDevice.DeviceAddress}");
            }
            catch (Exception e) {
                Console.WriteLine($"Exception {e}");
            }
        }

        Console.WriteLine("Ended");
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