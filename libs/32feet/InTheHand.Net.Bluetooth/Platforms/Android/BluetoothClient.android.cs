// 32feet.NET - Personal Area Networking for .NET
//
// InTheHand.Net.Bluetooth.AndroidBluetoothClient
// 
// Copyright (c) 2018-2024 In The Hand Ltd, All rights reserved.
// This source code is licensed under the MIT License

using Android.Bluetooth;
using Android.Content;
using Android.OS;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Bluetooth.Droid;
using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

namespace InTheHand.Net.Sockets
{
    public sealed class AndroidBluetoothClient : IBluetoothClient
    {
        private BluetoothSocket _socket;
        private readonly BluetoothRadio _radio;

        public AndroidBluetoothClient()
        {
            _radio = BluetoothRadio.Default;
            if (_radio is { Mode: RadioMode.PowerOff })
                _radio.Mode = RadioMode.Connectable;
        }

        internal AndroidBluetoothClient(BluetoothSocket socket) : this()
        {
            _socket = socket;
        }

        public IEnumerable<BluetoothDeviceInfo> PairedDevices
        {
            get
            {
                foreach (var device in ((BluetoothAdapter)_radio).BondedDevices)
                {
                    yield return new BluetoothDeviceInfo(new AndroidBluetoothDeviceInfo(device));
                }
            }
        }

        public IReadOnlyCollection<BluetoothDeviceInfo> DiscoverDevices(int maxDevices)
        {
            if (InTheHand.AndroidActivity.CurrentActivity == null)
                throw new NotSupportedException("CurrentActivity was not detected or specified");


            List<BluetoothDeviceInfo> devices = new List<BluetoothDeviceInfo>();

            HandlerThread handlerThread = new HandlerThread("ht");
            handlerThread.Start();
            Looper looper = handlerThread.Looper;
            Handler handler = new Handler(looper);

            BluetoothDiscoveryReceiver receiver = new BluetoothDiscoveryReceiver();
            IntentFilter filter = new IntentFilter();
            filter.AddAction(BluetoothDevice.ActionFound);
            filter.AddAction(BluetoothAdapter.ActionDiscoveryFinished);
            filter.AddAction(BluetoothAdapter.ActionDiscoveryStarted);
            InTheHand.AndroidActivity.CurrentActivity.RegisterReceiver(receiver, filter, null, handler);

            EventWaitHandle handle = new EventWaitHandle(false, EventResetMode.AutoReset);

            receiver.DeviceFound += (s, e) =>
            {
                var bdi = new BluetoothDeviceInfo(new AndroidBluetoothDeviceInfo(e));
                if (!devices.Contains(bdi))
                {
                    devices.Add(bdi);

                    if(devices.Count == maxDevices)
                    {
                        ((BluetoothAdapter)_radio).CancelDiscovery();
                    }
                }
            };

            ((BluetoothAdapter)_radio).StartDiscovery();

            receiver.DiscoveryComplete += (s, e) =>
            {
                InTheHand.AndroidActivity.CurrentActivity.UnregisterReceiver(receiver);
                handle.Set();
                handlerThread.QuitSafely();
            };

            handle.WaitOne();

            return devices.AsReadOnly();
        }

#if NET6_0_OR_GREATER
        public async IAsyncEnumerable<BluetoothDeviceInfo> DiscoverDevicesAsync([EnumeratorCancellation] CancellationToken cancellationToken)
        {
            if (InTheHand.AndroidActivity.CurrentActivity == null)
                throw new NotSupportedException("CurrentActivity was not detected or specified");
            
            List<BluetoothDeviceInfo> devices = new List<BluetoothDeviceInfo>();
            var waitable = new AutoResetEvent(false);

            HandlerThread handlerThread = new HandlerThread("ht");
            handlerThread.Start();
            Looper looper = handlerThread.Looper;
            Handler handler = new Handler(looper);

            BluetoothDiscoveryReceiver receiver = new BluetoothDiscoveryReceiver();
            IntentFilter filter = new IntentFilter();
            filter.AddAction(BluetoothDevice.ActionFound);
            filter.AddAction(BluetoothAdapter.ActionDiscoveryFinished);
            filter.AddAction(BluetoothAdapter.ActionDiscoveryStarted);
            InTheHand.AndroidActivity.CurrentActivity.RegisterReceiver(receiver, filter, null, handler);

            receiver.DeviceFound += (s, e) =>
            {
                var bdi = new BluetoothDeviceInfo(new AndroidBluetoothDeviceInfo(e));
                if (cancellationToken.IsCancellationRequested)
                {
                    ((BluetoothAdapter)_radio).CancelDiscovery();
                }
                else
                {
                    if (!devices.Contains(bdi))
                    {
                        devices.Add(bdi);
                        waitable.Set();
                    }
                }
            };

            ((BluetoothAdapter)_radio).StartDiscovery();

            // 1. Track how many devices we've already sent to the consumer
            int yieldedCount = 0;
            bool isFinished = false;

            receiver.DiscoveryComplete += (s, e) =>
            {
                isFinished = true;
                waitable.Set(); // Wake up one last time to drain the list
            };

            // 2. Change the loop condition: 
            // Continue if discovery is active OR if there are still items in our list to yield
            while (!isFinished || yieldedCount < devices.Count)
            {
                // Wait for a new device or the finished signal
                waitable.WaitOne(500); // Add a timeout to prevent deadlocks

                // 3. Yield everything we have collected so far
                while (yieldedCount < devices.Count)
                {
                    yield return devices[yieldedCount];
                    yieldedCount++;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    ((BluetoothAdapter)_radio).CancelDiscovery();
                    break;
                }
            }
        }
#endif

        public async Task<PairState> PairAsync(IBluetoothDeviceInfo device) {
            var nativeDevice = ((BluetoothAdapter)_radio).GetRemoteDevice(device.DeviceAddress.ToString("C"));

            if (nativeDevice.BondState == Bond.Bonded) {
                return PairState.AlreadyPaired;
            }
            
            var handlerThread = new HandlerThread("pair_ht");
            handlerThread.Start();
            var looper = handlerThread.Looper;
            var handler = new Handler(looper);
        
            var bondRecv = new BluetoothBondReceiver();
        
            var filter = new IntentFilter();
            filter.AddAction(BluetoothDevice.ActionBondStateChanged);
        
            AndroidActivity.CurrentActivity.RegisterReceiver(bondRecv, filter, null, handler);
            try {
                var bond = nativeDevice.CreateBond();
                Console.WriteLine($"Bond: {bond}");
                await bondRecv.BondTcs.Task;
            }
            finally {
                AndroidActivity.CurrentActivity.UnregisterReceiver(bondRecv);
                handlerThread.QuitSafely();
                handlerThread.Dispose();
            }

            return PairState.PairRejected;
        }
        
        public void Connect(BluetoothAddress address, Guid service)
        {
            var nativeDevice = ((BluetoothAdapter)_radio).GetRemoteDevice(address.ToString("C"));
            
            Console.WriteLine($"Device info: {nativeDevice.Name} bonded {nativeDevice.BondState}");
            if (!Authenticate && !Encrypt)
            {
                Console.WriteLine("Not encrypt or auth");
                try {
                    _socket = nativeDevice.CreateInsecureRfcommSocketToServiceRecord(
                        Java.Util.UUID.FromString(service.ToString()));

                }
                catch (Exception e) {
                    Console.WriteLine($"Exception creating insecure rfcomm {e.Message}");
                }
            }
            else
            {
                Console.WriteLine("Encrypt and or auth");
                _socket = nativeDevice.CreateRfcommSocketToServiceRecord(Java.Util.UUID.FromString(service.ToString()));
            }

            if (_socket != null)
            {
                ((BluetoothAdapter)_radio).CancelDiscovery();
                Console.WriteLine("Socket is not null");
                try
                {
                    _socket.Connect();
                }
                catch(Exception ex)
                {
                    Console.WriteLine($"Error connecting to socket: {ex} {ex.Message}");
                    System.Diagnostics.Debug.WriteLine(ex.Message);

                    try
                    {
                        Console.WriteLine("Trying again");
                        _socket.Connect();
                        AndroidNetworkStream.GetAvailable(_socket.InputStream as Android.Runtime.InputStreamInvoker);
                    }
                    catch (Exception ex2)
                    {
                        Console.WriteLine($"Another error {ex2.Message}");
                        System.Diagnostics.Debug.WriteLine(ex2.Message);

                        _socket = null;
                    }
                }
            }
        }

        public void Connect(BluetoothEndPoint remoteEP)
        {
            if (remoteEP == null)
                throw new ArgumentNullException(nameof(remoteEP));

            Connect(remoteEP.Address, remoteEP.Service);
        }

        public async Task ConnectAsync(BluetoothAddress address, Guid service)
        {
            Connect(address, service);
        }

        public void Close()
        {
            if (_socket is object)
            {
                if (_socket.IsConnected)
                {
                    _socket.Close();
                }

                _socket.Dispose();
                _socket = null;
            }
        }

        private bool _authenticate;

        public bool Authenticate { get => _authenticate; set => _authenticate = value; }

        Socket IBluetoothClient.Client => throw new PlatformNotSupportedException();

        public bool Connected => _socket is { IsConnected: true };

        private bool _encrypt;

        public bool Encrypt { get => _encrypt; set => _encrypt = value; }

        TimeSpan IBluetoothClient.InquiryLength { get => TimeSpan.Zero; set => throw new PlatformNotSupportedException(); }

        string IBluetoothClient.RemoteMachineName
        {
            get
            {
                if (_socket is { IsConnected: true })
                    return _socket.RemoteDevice.Name;

                return null;
            }
        }

        public NetworkStream GetStream()
        {
            if (Connected)
                return new AndroidNetworkStream(_socket.InputStream, _socket.OutputStream);

            return null;
        }

#region IDisposable Support
        private bool disposedValue = false; // To detect redundant calls

        void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                }

                disposedValue = true;
            }
        }

        // This code added to correctly implement the disposable pattern.
        public void Dispose()
        {
            // Do not change this code. Put cleanup code in Dispose(bool disposing) above.
            Dispose(true);
        }
        #endregion
        
        private class BluetoothBondReceiver : BroadcastReceiver {
            public TaskCompletionSource<PairState> BondTcs = new TaskCompletionSource<PairState>();
            public override void OnReceive(Context context, Intent intent)
            {
                if (intent.Action == BluetoothDevice.ActionBondStateChanged)
                {
                    // Get the device associated with the intent
                    var device = (BluetoothDevice)intent.GetParcelableExtra(BluetoothDevice.ExtraDevice);
            
                    // Get the current and previous bond states
                    int newState = intent.GetIntExtra(BluetoothDevice.ExtraBondState, (int)Bond.None);
                    int prevState = intent.GetIntExtra(BluetoothDevice.ExtraPreviousBondState, (int)Bond.None);
                    
                    System.Diagnostics.Debug.WriteLine($"Device: {device.Name}, Bond State: {newState}, Previous: {prevState}");

                    switch ((Bond)newState) {
                        case Bond.Bonded:
                            BondTcs.TrySetResult(PairState.PairAccepted);
                            break;
                        case Bond.None:
                            BondTcs.TrySetResult(PairState.PairRejected);
                            break;
                        case Bond.Bonding:
                            break;
                    }
                }
            }
        }
    }
}