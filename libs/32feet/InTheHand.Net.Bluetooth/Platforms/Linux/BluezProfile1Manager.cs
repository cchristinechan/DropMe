using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using InTheHand.Net.Sockets;
using Tmds.DBus;

namespace InTheHand.Net.Bluetooth.Platforms.Linux;

public class BluezProfile1Manager {
    private static Profile1 _instance;
    private static readonly object _lock = new object();
    private const string ServiceGuid = "bc8659c9-3aa7-4faf-ba42-c5feb93d1a3e";
    private BluezProfile1Manager() { }

    public static Profile1 Instance {
        get {
            lock (_lock) {
                if (_instance == null) {
                    _instance = new Profile1("DropMe", new Guid(ServiceGuid));
                    _instance.StartAsync().RunSynchronously();
                }
                return _instance;
            }
        }
    }
}

[DBusInterface("org.bluez.Profile1")]
public interface IProfile1 : IDBusObject {
    Task NewConnectionAsync(ObjectPath device, CloseSafeHandle fd, IDictionary<string, object> properties);
    Task RequestDisconnectionAsync(ObjectPath device);
    Task ReleaseAsync();
}

[DBusInterface("org.bluez.ProfileManager1")]
public interface IProfileManager1 : IDBusObject {
    Task RegisterProfileAsync(ObjectPath profile, string uuid, IDictionary<string, object> options);
    Task UnregisterProfileAsync(ObjectPath profile);
}

public class Profile1(string serviceName, Guid serviceGuid) : IProfile1 {
    public ObjectPath ObjectPath => new ObjectPath($"/com/32feet/{Process.GetCurrentProcess().Id}");
    private IProfileManager1 _profileManager;
    private readonly Guid _serviceGuid = serviceGuid;
    private readonly string _serviceName = serviceName;
    private readonly Dictionary<string, ConnectionReceived> _clientConnectedCallbacks = new();
    public event ConnectionReceived OnServerConnected;

    public delegate void ConnectionReceived(string macAddr, LinuxSocket socket);

    public void RegisterOnClientConnected(BluetoothAddress serverAddress, ConnectionReceived callback) {
        _clientConnectedCallbacks.Add(serverAddress.ToString("C"), callback);
    }

    public void UnregisterOnClientConnected(BluetoothAddress serverAddress) {
        _clientConnectedCallbacks.Remove(serverAddress.ToString("C"));
    }

    public async Task StartAsync() {
        var connection = new Connection("unix:path=/var/run/dbus/system_bus_socket");
        await connection.ConnectAsync().ConfigureAwait(false);

        await connection.RegisterObjectAsync(this).ConfigureAwait(false);

        _profileManager = await Task.Run(() => connection.CreateProxy<IProfileManager1>("org.bluez", "/org/bluez"))
            .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);

        var sdpOptions = new Dictionary<string, object>
        {
            { "Name", _serviceName },
            { "Role", "Dual" },
            { "Channel", (ushort)0 },
            { "Service", _serviceGuid.ToString() },
        };

        Console.WriteLine($"Registering SDP Profile for UUID {_serviceGuid}");
        await _profileManager.RegisterProfileAsync(ObjectPath, _serviceGuid.ToString(), sdpOptions);
        Console.WriteLine("SDP Profile registered successfully. Waiting for connections...");
    }

    public Task NewConnectionAsync(ObjectPath device, CloseSafeHandle fd, IDictionary<string, object> properties) {
        var path = device.ToString();

        // 1. Get the last part of the path (e.g., "dev_AA_BB_CC_DD_EE_FF")
        var devPart = path[(path.LastIndexOf('/') + 1)..];

        // 2. Remove the "dev_" prefix and replace underscores with colons
        var macAddress = devPart
            .Replace("dev_", "")
            .Replace("_", ":");

        Console.WriteLine($"New Connection from device: {device}");
        try {
            var handle = fd.DangerousGetHandle();

            var socket = new LinuxSocket(handle.ToInt32());

            if (_clientConnectedCallbacks.TryGetValue(macAddress, out var callback)) {
                callback(macAddress, socket);
            }
            else {
                OnServerConnected?.Invoke(macAddress, socket);
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"Error wrapping Bluetooth socket: {ex.Message}");
        }
        return Task.CompletedTask;
    }

    public Task RequestDisconnectionAsync(ObjectPath device) {
        Console.WriteLine($"Requesting Disconnection from device: {device}");
        return Task.CompletedTask;
    }

    public Task ReleaseAsync() {
        Console.WriteLine("Dbus releasing object");
        return Task.CompletedTask;
    }
}