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
using InTheHand.Net.Bluetooth.Platforms.Linux;
using Tmds.DBus;

namespace InTheHand.Net.Sockets {
    public sealed class LinuxBluetoothListener : IBluetoothListener {
        private TaskCompletionSource<LinuxSocket> _socketTcs = new();
        public bool Active { get; private set; }

        public ServiceClass ServiceClass { get; set; }
        public string ServiceName { get; set; }
        public ServiceRecord ServiceRecord { get; set; }
        public Guid ServiceUuid { get; set; }
        public void Start() {
            BluezProfile1Manager.Instance.OnServerConnected += OnClientConnected;
            Active = true;
        }

        public void Stop() {
            if (Active) {
                if (!_socketTcs.Task.IsCompleted)
                    (_socketTcs.Task.GetAwaiter().GetResult()).Close();
                _socketTcs = new TaskCompletionSource<LinuxSocket>();
                BluezProfile1Manager.Instance.OnServerConnected -= OnClientConnected;
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

        public BluetoothClient AcceptBluetoothClient() {
            return new BluetoothClient(new LinuxBluetoothClient((LinuxSocket)AcceptSocket()));
        }

        public async Task<BluetoothClient> AcceptBluetoothClientAsync() {
            return new BluetoothClient(new LinuxBluetoothClient((LinuxSocket)await AcceptSocketAsync()));
        }

        private void OnClientConnected(string clientAddr, LinuxSocket clientSocket) {
            if (!_socketTcs.TrySetResult(clientSocket)) {
                Console.WriteLine($"Dropping socket {clientSocket} as one has already been accepted");
            }
        }
    }
}
