using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace DropMe.Services.Session;

public class ConnectionManager(CancellationToken sessionCt) : IDisposable {
    public bool TcpConnected => _tcpConnection?.connection is { IsConnected: true };
    public bool BluetoothConnected => _bluetoothConnection?.connection is { IsConnected: true };
    public string? PeerName => _connectionInUse?.PeerName;
    public bool IsConnected => TcpConnected || BluetoothConnected;
    public byte[]? AesSessionKey { get; set; }
    public event Action<ConnectionEndReason>? ConnectionEnded;
    private const string DropMeGuid = "bc8659c9-3aa7-4faf-ba42-c5feb93d1a3e";
    private (EncryptedConnection<TcpClientNsAdapter> connection, Task receiveTask)? _tcpConnection;
    private (EncryptedConnection<BluetoothClientNsAdapter> connection, Task receiveTask)? _bluetoothConnection;
    private readonly SemaphoreSlim _connectionInUseSemaphore = new(1, 1);
    private IConnection? _connectionInUse; // Points to one of the above connections
    private readonly Channel<(IConnection, DropMeMsg)> _networkSyncChannel = Channel.CreateUnbounded<(IConnection, DropMeMsg)>();
    private Task? _formSecondaryConnectionsTask;

    public async Task TryAcceptTcpConnection(IPEndPoint listenEp, CancellationToken ct) {
        _connectionInUse = await AcceptTcpConnection(listenEp, ct).ConfigureAwait(false);
    }

    public async Task TryAcceptTcpAndBtConnections(IPEndPoint listenEp, CancellationToken ct) {
        _connectionInUse = await WaitForOneAndBackgroundOther(token => AcceptTcpConnection(listenEp, token),
            AcceptBluetoothConnection, ct);
    }

    public async Task EstablishConnections(IPEndPoint? lanServerEp, BluetoothAddress? btAddr, string? btName,
        CancellationToken ct) {
        Func<CancellationToken, Task<IConnection?>>? establishBtConnFactory = null;
        Func<CancellationToken, Task<IConnection?>>? establishTcpConnFactory = null;

        if (lanServerEp is not null)
            establishTcpConnFactory = token => EstablishTcpConnection(lanServerEp, token);

        if (!string.IsNullOrEmpty(btName)) {
            if (btAddr is not null) {
                establishBtConnFactory = token => EstablishBtConnection(btAddr, btName, token);
            }
            else {
                establishBtConnFactory = token => EstablishBtConnection(btName, token);
            }
        }

        if (establishTcpConnFactory is not null && establishBtConnFactory is not null) {
            Console.WriteLine("Trying to establish TCP and BT connection");
            _connectionInUse = await WaitForOneAndBackgroundOther(establishTcpConnFactory, establishBtConnFactory, ct);
        }
        else if (establishTcpConnFactory is not null) {
            Console.WriteLine("Trying to establish TCP connection");
            while (true) {
                try {
                    _connectionInUse = await establishTcpConnFactory(ct).ConfigureAwait(false);
                    break;
                }
                catch (Exception e) {
                    Console.WriteLine($"Error establishing tcp connection {e}, retrying");
                }
            }
        }
        else if (establishBtConnFactory is not null) {
            Console.WriteLine("Trying to establish BT connection");
            while (true) {
                try {
                    _connectionInUse = await establishBtConnFactory(ct).ConfigureAwait(false);
                    break;
                }
                catch (Exception e) {
                    Console.WriteLine($"Error establishing bt connection {e}, retrying");
                }
            }
        }
        else {
            throw new ArgumentException("Not enough details to form any connection");
        }
    }

    private async Task<T> WaitForOneAndBackgroundOther<T>(Func<CancellationToken, Task<T>> t1Factory, Func<CancellationToken, Task<T>> t2Factory, CancellationToken ct) {
        var t1 = t1Factory(ct);
        var t2 = t2Factory(ct);
        while (true) {
            var completed = await Task.WhenAny(t1, t2).ConfigureAwait(false);
            if (!completed.IsCompletedSuccessfully) {
                if (completed == t1) {
                    try {
                        await t1.ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        Console.WriteLine($"T1 failed {e}");
                    }
                    t1 = t1Factory(ct);
                }
                else if (completed == t2) {
                    try {
                        await t2.ConfigureAwait(false);
                    }
                    catch (Exception e) {
                        Console.WriteLine($"T2 {e}");
                    }
                    t2 = t2Factory(ct);
                }
            }
            else {
                if (completed == t1) {
                    _formSecondaryConnectionsTask = t2;
                    return await t1.ConfigureAwait(false);
                }
                else if (completed == t2) {
                    _formSecondaryConnectionsTask = t1;
                    return await t2.ConfigureAwait(false);
                }
            }
        }
    }

    /// <summary>
    /// Sends a message while trying to recover the connection if it's lost
    /// </summary>
    /// <param name="msg"></param>
    /// <param name="ct"></param>
    public async Task SendMessage(DropMeMsg msg, CancellationToken ct) {
        Console.WriteLine($"Connection manager forwarding message {msg}");
        await _connectionInUseSemaphore.WaitAsync(ct).ConfigureAwait(false);

        if (_connectionInUse is null) {
            await TryRecoverConnection(ct);
        }
        
        while (IsConnected) {
            // Connection not null as this is checked before loop and semaphore is held, TryRecoverConnection will throw if it can't make it not null
            try {
                var ctsWithTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                await _connectionInUse!.SendMessageAsync(msg, ctsWithTimeout.Token).ConfigureAwait(false);
                Console.WriteLine($"Message {msg} sent");
                break;
            }
            catch (Exception e) {
                Console.WriteLine($"Failed to send message {msg} {e.Message}");
                _connectionInUse!.Dispose();
                _connectionInUse = null;
                await TryRecoverConnection(ct);
            }
        }

        _connectionInUseSemaphore.Release();
    }

    // Should be called with semaphore held
    private async Task TryRecoverConnection(CancellationToken ct) {
        if (TcpConnected) {
            Console.WriteLine("Fell back to TCP");
            _connectionInUse = _tcpConnection!.Value.connection;
        }
        else if (BluetoothConnected) {
            Console.WriteLine("Fell back to BT");
            _connectionInUse = _bluetoothConnection!.Value.connection;
        }
        if (IsConnected) {
            // Notify peer of the change
            await _connectionInUse!.SendMessageAsync(new SwitchConnectionRequest(), ct)
                .ConfigureAwait(false);
        }
        else {
            Console.WriteLine("Could not recover connection");
            _networkSyncChannel.Writer.Complete();
            
            ConnectionEnded?.Invoke(ConnectionEndReason.AllChannelsDisconnected);
        }

        
    }

    public async IAsyncEnumerable<FileMsg> ReceiveMessages() {
        await foreach (var (conn, message) in _networkSyncChannel.Reader.ReadAllAsync()) {
            // Perform quick check on the connections and if they have a fault close them properly,
            // they'll only complete if the networksyncchannel gets closed but worth checking
            var tcpRecvTask = _tcpConnection?.receiveTask;
            if (tcpRecvTask?.IsFaulted is true || tcpRecvTask?.IsCompleted is true) {
                Console.WriteLine("Receive method is dropping TCP");
                try {
                    await tcpRecvTask.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    Console.WriteLine(ex);
                }
                _tcpConnection?.connection.Dispose();
                _tcpConnection = null;
            }
            var btRecvTask = _bluetoothConnection?.receiveTask;
            if (btRecvTask?.IsFaulted is true || btRecvTask?.IsCompleted is true) {
                Console.WriteLine("Receive method is dropping BT");
                try {
                    await btRecvTask.ConfigureAwait(false);
                }
                catch (Exception ex) {
                    Console.WriteLine(ex);
                }
                _bluetoothConnection?.connection.Dispose();
                _bluetoothConnection = null;
            }

            if (!IsConnected) {
                ConnectionEnded?.Invoke(ConnectionEndReason.AllChannelsDisconnected);
            }

            switch (message) {
                case ControlMsg ctrlMsg:
                    if (message is not PingMsg && message is not PongMsg)
                        Console.WriteLine($"Received non ka control message {message}");
                    await RespondToControlMessage(conn, ctrlMsg);
                    break;
                case FileMsg dataMsg:
                    Console.WriteLine("Returning a data message");
                    yield return dataMsg;
                    break;
            }
        }
    }

    private async Task<IConnection> AcceptTcpConnection(IPEndPoint listenEp, CancellationToken ct) {
        Console.WriteLine("Waiting for TCP connections");
        var listener = new TcpListener(listenEp);
        listener.Start();
        while (!TcpConnected) {
            var client = await listener.AcceptTcpClientAsync(ct);

            if (AesSessionKey is null)
                throw new NullReferenceException("Aes session key must be set before connecting");

            var connection = new EncryptedConnection<TcpClientNsAdapter>(new TcpClientNsAdapter(client), client.Client.RemoteEndPoint?.ToString() ?? "Unknown", AesSessionKey);
            connection.OnDisconnect += OnConnectionDisconnected;
            if (!await connection.ServerConnectionHandshake(ct).ConfigureAwait(false)) {
                // Client did not perform correct handshake so close it and keep listening
                client.Close();
            }
            else {
                Console.WriteLine("Created a connection, handshake successful");
                var task = connection.StartHandlingMessages(_networkSyncChannel.Writer, sessionCt);
                _tcpConnection = (connection, task);
            }
        }
        Console.WriteLine("Stopping TCP listening");
        listener.Stop();
        return _tcpConnection!.Value.connection;
    }

    private async Task<IConnection> AcceptBluetoothConnection(CancellationToken ct) {
        using var listener = new BluetoothListener(new Guid(DropMeGuid));
        try {
            var radio = BluetoothRadio.Default;
            radio.Mode = RadioMode.Discoverable;
            listener.Start();
            Console.WriteLine("Bluetooth server started, waiting for connections...");

            while (!BluetoothConnected) {
                var client = await listener.AcceptBluetoothClientAsync();
                Console.WriteLine($"Client {client.RemoteMachineName} connected!");
                radio.Mode = RadioMode.Connectable;

                if (AesSessionKey is null)
                    throw new NullReferenceException("Aes session key must be set before connecting");
                var connection = new EncryptedConnection<BluetoothClientNsAdapter>(new BluetoothClientNsAdapter(client),
                    client.RemoteMachineName, AesSessionKey);
                connection.OnDisconnect += OnConnectionDisconnected;
                if (!await connection.ServerConnectionHandshake(ct).ConfigureAwait(false)) {
                    Console.WriteLine($"Client {connection.PeerName} tried to connect with a bad handshake");
                    client.Close();
                }
                else {
                    Console.WriteLine("Created a connection, handshake successful");
                    var task = connection.StartHandlingMessages(_networkSyncChannel.Writer, sessionCt);
                    _bluetoothConnection = (connection, task);
                    Console.WriteLine("Accepted a BT connection!");
                }
            }
        }
        finally {
            listener.Stop();
        }
        return _bluetoothConnection!.Value.connection;
    }

    private async Task<IConnection?> EstablishTcpConnection(IPEndPoint serverEp, CancellationToken ct) {
        var client = new TcpClient();
        Console.WriteLine($"Trying to connect to {serverEp}");
        await client.ConnectAsync(serverEp, ct).ConfigureAwait(false);
        Console.WriteLine($"Connected to {serverEp}");
        if (AesSessionKey is null)
            throw new NullReferenceException("Aes session key must be set before connecting");
        var connection = new EncryptedConnection<TcpClientNsAdapter>(new TcpClientNsAdapter(client), serverEp.Address.ToString(), AesSessionKey);
        connection.OnDisconnect += OnConnectionDisconnected;
        if (!await connection.ClientConnectionHandshake(ct).ConfigureAwait(false)) {
            Console.WriteLine("Server did not complete handshake properly");
            client.Close();
            return null;
        }
        else {
            Console.WriteLine("Created a connection, handshake successful");
            var task = connection.StartHandlingMessages(_networkSyncChannel.Writer, sessionCt);
            Console.WriteLine("Started handling messages");
            _tcpConnection = (connection, task);
            await _connectionInUseSemaphore.WaitAsync().ConfigureAwait(false);
            
            return connection;
        }
    }

    private async Task<IConnection?> EstablishBtConnection(BluetoothAddress knownAddress, string knownName, CancellationToken ct) {
        // Maybe fall back to known name
        var client = new BluetoothClient();
        client.Encrypt = true;
        client.Authenticate = true;
        Console.WriteLine($"Attempting to pair with known address {knownAddress}");
        var pairResult = await client.PairAsync(knownAddress).ConfigureAwait(false);
        Console.WriteLine($"Pairing: {pairResult}");

        Console.WriteLine($"Attempting to connect to known address {knownAddress}");
        await client.ConnectAsync(knownAddress, new Guid(DropMeGuid)).ConfigureAwait(false);
        Console.WriteLine($"Connected to {knownAddress}");

        if (AesSessionKey is null)
            throw new NullReferenceException("Aes session key must be set before connecting");

        var connection = new EncryptedConnection<BluetoothClientNsAdapter>(new BluetoothClientNsAdapter(client), client.RemoteMachineName, AesSessionKey);
        connection.OnDisconnect += OnConnectionDisconnected;
        if (!await connection.ClientConnectionHandshake(ct).ConfigureAwait(false)) {
            Console.WriteLine("Bluetooth server didn't perform handshake correctly");
            client.Close();
            return null;
        }
        else {
            Console.WriteLine("Created a connection, handshake successful");
            var task = connection.StartHandlingMessages(_networkSyncChannel.Writer, sessionCt);
            _bluetoothConnection = (connection, task);
            return connection;
        }
    }

    private async Task<IConnection?> EstablishBtConnection(string knownName, CancellationToken ct) {
        var serviceGuid = new Guid(DropMeGuid);
        var client = new BluetoothClient();
        client.Encrypt = true;
        client.Authenticate = true;
        Console.WriteLine("Discovering nearby Bluetooth devices...");
        BluetoothDeviceInfo? matchedDevice = null;
        // Check each device for the target service
        var devices = client.DiscoverDevicesAsync(ct).ConfigureAwait(false);
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
                // Name must match and must be advertising dropme
                if (device.DeviceName == knownName && (await device.GetRfcommServicesAsync(false).ConfigureAwait(false)).Any(s => s == serviceGuid)) {
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

        // Make work with multiple devices
        if (matchedDevice != null) {
            Console.WriteLine("Attempting to connect to the device");
            try {
                var success = await client.PairAsync(matchedDevice.DeviceAddress).ConfigureAwait(false);
                Console.WriteLine($"Pairing: {success}");
                await client.ConnectAsync(matchedDevice.DeviceAddress, serviceGuid).ConfigureAwait(false);
                Console.WriteLine($"Bluetooth connected to {matchedDevice.DeviceAddress}");

                if (AesSessionKey is null)
                    throw new NullReferenceException("Aes session key must be set before connecting");

                var connection = new EncryptedConnection<BluetoothClientNsAdapter>(new BluetoothClientNsAdapter(client), client.RemoteMachineName, AesSessionKey);
                connection.OnDisconnect += OnConnectionDisconnected;
                if (!await connection.ClientConnectionHandshake(ct).ConfigureAwait(false)) {
                    // TRY NEXT DEVICE INSTEAD OF DYING
                    client.Close();
                    return null;
                }
                Console.WriteLine("Created a connection, handshake successful");
                var task = connection.StartHandlingMessages(_networkSyncChannel.Writer, sessionCt);
                _bluetoothConnection = (connection, task);
                return connection;
            }
            catch (Exception e) {
                Console.WriteLine($"Exception {e}");
            }
        }

        Console.WriteLine("Ended bluetooth discovery");
        return null;
    }

    private async Task RespondToControlMessage(IConnection conn, ControlMsg message) {
        switch (message) {
            case PingMsg:
                var pongMsg = new PongMsg();
                // Make sure this message is sent on the receiving channel
                await conn.SendMessageAsync(pongMsg, CancellationToken.None).ConfigureAwait(false);
                break;
            case PongMsg:
                // No action needs taken
                break;
            case SwitchConnectionRequest:
                // No need to dispose old connection in use as it may not necessarily be dead, may be switching for speed reasons
                await _connectionInUseSemaphore.WaitAsync();
                _connectionInUse = conn;
                _connectionInUseSemaphore.Release();
                break;
            case DisconnectMsg:
                ConnectionEnded?.Invoke(ConnectionEndReason.PeerRequested);
                _networkSyncChannel.Writer.Complete();
                break;
            default: throw new Exception("Unknown message");
        }
    }

    private async void OnConnectionDisconnected(IConnection conn, IEnumerable<DropMeMsg> undelivered) {
        Console.WriteLine($"Connection manager noticed connection {conn} is dead");
        if (conn is EncryptedConnection<TcpClientNsAdapter> && _tcpConnection is not null) {
            _tcpConnection.Value.connection.Dispose();
            _tcpConnection = null;
        }
        else if (conn is EncryptedConnection<BluetoothClientNsAdapter> && _bluetoothConnection is not null) {
            _bluetoothConnection.Value.connection.Dispose();
            _bluetoothConnection = null;
        }

        //await _connectionInUseSemaphore.WaitAsync().ConfigureAwait(false);
        try {
            await TryRecoverConnection(CancellationToken.None).ConfigureAwait(false);
            foreach (var msg in undelivered) {
                Console.WriteLine($"Resending {msg}");
                await _connectionInUse!.SendMessageAsync(msg, CancellationToken.None)
                    .ConfigureAwait(false); // Not null as tryrecoverconnection would have thrown
            }
        }
        catch (Exception) {
            
        } // Ignore as this is a best effort recovery
        finally {
            //_connectionInUseSemaphore.Release();
        }
    }

    public void Dispose() {
        _tcpConnection?.connection.Dispose();
        _bluetoothConnection?.connection.Dispose();
    }
}

public enum ConnectionEndReason {
    AllChannelsDisconnected,
    PeerRequested
}