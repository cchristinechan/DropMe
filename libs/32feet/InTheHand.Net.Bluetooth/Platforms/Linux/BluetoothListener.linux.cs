// 32feet.NET - Personal Area Networking for .NET
//
// InTheHand.Net.Sockets.BluetoothListener (Linux)
// 
// Copyright (c) 2018-2023 In The Hand Ltd, All rights reserved.
// This source code is licensed under the MIT License

using InTheHand.Net.Bluetooth;
using InTheHand.Net.Bluetooth.Sdp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Sockets;
using System.Threading.Tasks;
using Tmds.DBus;

namespace InTheHand.Net.Sockets
{
    public sealed class LinuxBluetoothListener : IBluetoothListener {
        private TaskCompletionSource<LinuxSocket> _socketTcs = new();
        public bool Active { get; private set; }

        public ServiceClass ServiceClass { get; set; }
        public string ServiceName { get; set; }
        public ServiceRecord ServiceRecord { get; set; }
        public Guid ServiceUuid { get; set; }
        private BluetoothSdpServer _sdpServer;
        public void Start()
        {
            // Validate that the object path is a valid dbus object path
            var objPathEnd = string.IsNullOrEmpty(ServiceName) ? Process.GetCurrentProcess().Id.ToString() : ServiceName;
            var dbusObjectPath = $"/com/32feet/{objPathEnd}";
            _sdpServer = new BluetoothSdpServer(dbusObjectPath);
            _sdpServer.OnClientConnected += OnClientConnected;
            Task.Run(async () => await _sdpServer.StartAsync(ServiceUuid)).Wait();
            Console.WriteLine($"Sdp server advertising {ServiceUuid} using dbus object {dbusObjectPath} for bluez communication");
            Active = true;
        }

        public void Stop()
        {
            if (Active)
            {
                (_socketTcs.Task.GetAwaiter().GetResult()).Close();
                _socketTcs = new TaskCompletionSource<LinuxSocket>();
                Active = false;
            }
        }

        public bool Pending() {
            return Active && !_socketTcs.Task.IsCompleted;
        }

        public Socket AcceptSocket() {
            return _socketTcs.Task.GetAwaiter().GetResult();
        }

        public async Task<Socket> AcceptSocketAsync() {
            var socket = await _socketTcs.Task;
            return socket;
        }
        
        public BluetoothClient AcceptBluetoothClient()
        {
            return new BluetoothClient(new LinuxBluetoothClient((LinuxSocket)AcceptSocket()));
        }

        public async Task<BluetoothClient> AcceptBluetoothClientAsync()
        {
            return new BluetoothClient(new LinuxBluetoothClient((LinuxSocket) await AcceptSocketAsync()));
        }

        private void OnClientConnected(Socket clientSocket) {
            if (!_socketTcs.TrySetResult((LinuxSocket)clientSocket)) {
                Console.WriteLine($"Dropping socket {clientSocket} as one has already been accepted");
            }
        }
    }
    
    [DBusInterface("org.bluez.Profile1")]
    public interface IProfile1 : IDBusObject
    {
        Task NewConnectionAsync(ObjectPath device, CloseSafeHandle fd, IDictionary<string, object> properties);
        Task RequestDisconnectionAsync(ObjectPath device);
        Task ReleaseAsync();
    }

    [DBusInterface("org.bluez.ProfileManager1")]
    public interface IProfileManager1 : IDBusObject
    {
        Task RegisterProfileAsync(ObjectPath profile, string uuid, IDictionary<string, object> options);
        Task UnregisterProfileAsync(ObjectPath profile);
    }

    public class BluetoothSdpServer(string objPath, string serviceName) : IProfile1 {
        public delegate void ClientConnectedHandler(Socket clientSocket);
        public event ClientConnectedHandler OnClientConnected;
        public ObjectPath ObjectPath { get; } = new(objPath);
        private IProfileManager1 _profileManager;

        public async Task StartAsync(Guid guid)
        {
            var connection = new Connection("unix:path=/var/run/dbus/system_bus_socket");
            await connection.ConnectAsync().ConfigureAwait(false);

            await connection.RegisterObjectAsync(this).ConfigureAwait(false);

            _profileManager = await Task.Run(() => connection.CreateProxy<IProfileManager1>("org.bluez", "/org/bluez"))
                .WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
            string service;
            if (string.IsNullOrEmpty(serviceName)) {
                service = "32feet sdp service";
            }
            else {
                service = serviceName;
            }
            var sdpOptions = new Dictionary<string, object>
            {
                { "Name", service },
                { "Role", "Server" },
                { "Channel", (ushort)0 },
                { "Service", guid.ToString() }
            };

            Console.WriteLine($"Registering SDP Profile for UUID {guid}");
            await _profileManager.RegisterProfileAsync(ObjectPath, guid.ToString(), sdpOptions);
            Console.WriteLine("SDP Profile registered successfully. Waiting for connections...");
        }

        public async Task StopAsync() {
            await _profileManager.UnregisterProfileAsync(ObjectPath);
            Console.WriteLine("Successfully unregistered SDP profile");
        }

        public Task NewConnectionAsync(ObjectPath device, CloseSafeHandle fd, IDictionary<string, object> properties)
        {
            Console.WriteLine($"New Connection from device: {device}");
            try 
            {
                var handle = fd.DangerousGetHandle();

                var socket = new LinuxSocket(handle.ToInt32());

                OnClientConnected?.Invoke(socket);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error wrapping Bluetooth socket: {ex.Message}");
            }

            return Task.CompletedTask;
        }

        public Task ReleaseAsync() 
        {
            return Task.CompletedTask;
        }

        public Task RequestDisconnectionAsync(ObjectPath device) => Task.CompletedTask;
    }
}
