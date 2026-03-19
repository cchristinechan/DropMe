using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;
using InTheHand.Net.Sockets;

namespace DropMe.Services.Session;

public enum ChannelToken {
    TcpConnection, BluetoothConnection
}

public class SessionManager(IStorageService storageService) : IDisposable {
    public Func<FileOfferInfo, Task<bool>>? FileOfferDecision { get; set; }
    public bool IsConnected => TcpConnected || BluetoothConnected;
    public bool TcpConnected => _tcpConnection?.connection is { IsConnected: true };
    public bool BluetoothConnected => _bluetoothConnection?.connection is { IsConnected: true };
    public string? PeerName => _connectionInUse?.PeerName;
    public event Action<string>? FileSaved;
    public event Action<Guid, string /*sha256 hex*/>? FileAcked;
    public event Action<SessionEndReason>? SessionEnded;
    public byte[]? AesSessionKey { get; set; } // MUST BE SET BEFORE A CONNECTION IS ESTABLISHED
    private const string DropMeGuid = "bc8659c9-3aa7-4faf-ba42-c5feb93d1a3e";
    private (EncryptedConnection<TcpClientNsAdapter> connection, Task receiveTask)? _tcpConnection;
    private (EncryptedConnection<BluetoothClientNsAdapter> connection, Task receiveTask)? _bluetoothConnection;
    private IConnection? _connectionInUse; // Points to one of the above connections
    private CancellationTokenSource _sessionCtSource = new();
    private readonly Channel<(ChannelToken, DropMeMsg)> _networkSyncChannel = Channel.CreateUnbounded<(ChannelToken, DropMeMsg)>();
    private readonly Dictionary<Guid, FileTransferState> _fileTransferStates = new Dictionary<Guid, FileTransferState>();

    public async Task<bool> TryAcceptTcpConnection(IPEndPoint listenEp, CancellationToken ct) {
        Console.WriteLine("Waiting for TCP connections");
        var listener = new TcpListener(listenEp);
        listener.Start();
        var client = await listener.AcceptTcpClientAsync(ct);
        listener.Stop();

        if (AesSessionKey is null)
            throw new NullReferenceException("Aes session key must be set before connecting");

        var connection = new EncryptedConnection<TcpClientNsAdapter>(new TcpClientNsAdapter(client), client.Client.RemoteEndPoint?.ToString() ?? "Unknown", AesSessionKey);
        if (!await connection.ServerConnectionHandshake(ct).ConfigureAwait(false))
            return false;
        Console.WriteLine("Created a connection, handshake successful");
        var task = connection.StartHandlingMessages(_networkSyncChannel.Writer, ChannelToken.TcpConnection, _sessionCtSource.Token);
        _tcpConnection = (connection, task);
        _connectionInUse = connection; // TODO: Negotiate a connection
        Console.WriteLine($"Accepted a TCP connection {TcpConnected}!");
        return TcpConnected;
    }

    public async Task<bool> TryEstablishTcpConnection(IPEndPoint serverEp, CancellationToken ct) {
        var client = new TcpClient();
        Console.WriteLine($"Trying to connect to {serverEp}");
        await client.ConnectAsync(serverEp, ct).ConfigureAwait(false);
        Console.WriteLine($"Connected to {serverEp}");
        var stream = client.GetStream();
        Console.WriteLine("Got it's stream");
        if (AesSessionKey is null)
            throw new NullReferenceException("Aes session key must be set before connecting");
        var connection = new EncryptedConnection<TcpClientNsAdapter>(new TcpClientNsAdapter(client), serverEp.Address.ToString(), AesSessionKey);
        if (!await connection.ClientConnectionHandshake(ct).ConfigureAwait(false))
            return false;
        Console.WriteLine("Created a connection, handshake successful");
        var task = connection.StartHandlingMessages(_networkSyncChannel.Writer, ChannelToken.TcpConnection, _sessionCtSource.Token);
        Console.WriteLine("Started handling messages");
        _tcpConnection = (connection, task);
        _connectionInUse = connection; // TODO: Negotiate a connection
        Console.WriteLine("Returning");
        return TcpConnected;
    }

    public async Task<bool> TryAcceptBluetoothConnection(CancellationToken ct) {
        try {
            using var listener = new BluetoothListener(new Guid(DropMeGuid));
            var radio = BluetoothRadio.Default;
            if (radio is null || radio.Mode == RadioMode.PowerOff) {
                Console.WriteLine("Bluetooth not available while accepting a connection.");
                return false;
            }
            radio.Mode = RadioMode.Discoverable;
            listener.Start();
            Console.WriteLine("Bluetooth server started, waiting for connections...");

            using var client = await listener.AcceptBluetoothClientAsync();
            Console.WriteLine($"Client {client.RemoteMachineName} connected!");
            radio.Mode = RadioMode.Connectable;

            if (AesSessionKey is null)
                throw new NullReferenceException("Aes session key must be set before connecting");

            var connection = new EncryptedConnection<BluetoothClientNsAdapter>(new BluetoothClientNsAdapter(client), client.RemoteMachineName, AesSessionKey);
            if (!await connection.ServerConnectionHandshake(ct).ConfigureAwait(false))
                return false;
            Console.WriteLine("Created a connection, handshake successful");
            var task = connection.StartHandlingMessages(_networkSyncChannel.Writer, ChannelToken.BluetoothConnection, _sessionCtSource.Token);
            _bluetoothConnection = (connection, task);
            Console.WriteLine("Accepted a BT connection!");
            return BluetoothConnected;
        }
        catch (Exception ex) {
            Console.WriteLine($"Bluetooth accept failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TryEstablishBluetoothConnection(BluetoothAddress knownAddress, string knownName, CancellationToken ct) {
        try {
            using var client = new BluetoothClient();
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
            if (!await connection.ClientConnectionHandshake(ct).ConfigureAwait(false))
                return false;
            Console.WriteLine("Created a connection, handshake successful");
            var task = connection.StartHandlingMessages(_networkSyncChannel.Writer, ChannelToken.BluetoothConnection, _sessionCtSource.Token);
            _bluetoothConnection = (connection, task);
            return BluetoothConnected;
        }
        catch (Exception ex) {
            Console.WriteLine($"Bluetooth connect to known address failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TryEstablishBluetoothConnection(string knownName, CancellationToken ct) {
        try {
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
                    Console.WriteLine($"Bluetooth connected to {matchedDevice.DeviceAddress}");

                    if (AesSessionKey is null)
                        throw new NullReferenceException("Aes session key must be set before connecting");

                    var connection = new EncryptedConnection<BluetoothClientNsAdapter>(new BluetoothClientNsAdapter(client), client.RemoteMachineName, AesSessionKey);
                    if (!await connection.ClientConnectionHandshake(ct).ConfigureAwait(false))
                        return false;
                    Console.WriteLine("Created a connection, handshake successful");
                    var task = connection.StartHandlingMessages(_networkSyncChannel.Writer, ChannelToken.BluetoothConnection, _sessionCtSource.Token);
                    _bluetoothConnection = (connection, task);
                }
                catch (Exception e) {
                    Console.WriteLine($"Exception {e}");
                }
            }

            Console.WriteLine("Ended bluetooth discovery");
            return BluetoothConnected;
        }
        catch (Exception ex) {
            Console.WriteLine($"Bluetooth discover-and-connect failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> Receive() {

        await foreach (var (token, message) in _networkSyncChannel.Reader.ReadAllAsync()) {
            Console.WriteLine($"Received: {message}");
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
            var btRecvTask = _tcpConnection?.receiveTask;
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
                SessionEnded?.Invoke(SessionEndReason.AllChannelsDisconnected);
                return false;
            }

            Console.WriteLine("Responding to a message");
            switch (message) {
                case ControlMsg ctrlMsg:
                    await RespondToControlMessage(token, ctrlMsg);
                    break;
                case FileMsg dataMsg:
                    await RespondToDataMessage(token, dataMsg);
                    break;
            }
        }

        return true;
    }

    public async Task StopSession() {
        Console.WriteLine("Stop session called");
        // Try to notify peer that a disconnection has been requested
        await SendMessage(new DisconnectMsg(), CancellationToken.None);

        await _sessionCtSource.CancelAsync();
        if (_tcpConnection is var (tcpConn, _)) {
            tcpConn.Dispose();
            _tcpConnection = null;
        }

        if (_bluetoothConnection is var (btConn, _)) {
            btConn.Dispose();
            _bluetoothConnection = null;
        }
        _sessionCtSource = new CancellationTokenSource();
    }

    // There would be race conditions if multiple threads try to modify the same _fileTransferStates[fileId]
    // This should be fine as after it is SendInProgress, this thread is the only one modifying that fileId
    public async Task SendFileAsync(Stream file, string filename, CancellationToken ct) {
        var fileId = Guid.NewGuid();
        var offer = new FileOfferInfo(fileId, filename, file.Length);
        await SendMessage(new FileOfferMsg(offer), ct).ConfigureAwait(false);
        var tcs = new TaskCompletionSource<bool>();
        lock (_fileTransferStates) {
            _fileTransferStates.Add(fileId, new AwaitingDecision(offer, tcs));
        }
        Console.WriteLine("Awaiting acceptance");
        var fileAccepted = await tcs.Task;
        Console.WriteLine("File accepted");

        if (!fileAccepted)
            throw new InvalidOperationException("Peer rejected the file.");

        var fileState = new SendInProgress(file, IncrementalHash.CreateHash(HashAlgorithmName.SHA256), 0);
        lock (_fileTransferStates) {
            _fileTransferStates[fileId] = fileState;
        }

        const int chunkSize = 64 * 1024;
        var buffer = new byte[chunkSize];
        var chunkIndex = 0;

        while (true) {
            var n = await file.ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
            if (n <= 0) break;

            fileState.Hash.AppendData(buffer, 0, n);

            await SendMessage(new FileChunkMsg(fileId, chunkIndex++, buffer[0..n]), ct).ConfigureAwait(false);
        }

        var hash = fileState.Hash.GetHashAndReset(); // 32 bytes
        lock (_fileTransferStates) {
            _fileTransferStates[fileId] = new AwaitingAck(hash);
        }

        Debug.Assert(hash.Length == 32, "Sha256 hash should be 32 bytes");
        await SendMessage(new FileDoneMsg(fileId, hash), ct).ConfigureAwait(false);
    }

    private async Task SendMessage(DropMeMsg msg, CancellationToken ct) {
        Console.WriteLine($"Sending a message {msg}");
        // Make it try to find a valid connection?
        if (_connectionInUse is not null) {
            await _connectionInUse.SendMessageAsync(msg, ct).ConfigureAwait(false);
        }
        else {
            throw new Exception("Unable to resolve a valid connection");
        }
    }

    private async Task RespondToControlMessage(ChannelToken token, ControlMsg message) {
        switch (message) {
            case PingMsg:
                var pongMsg = new PongMsg();
                switch (token) {
                    case ChannelToken.TcpConnection:
                        await _tcpConnection!.Value.connection.SendMessageAsync(pongMsg, CancellationToken.None).ConfigureAwait(false);
                        break;
                    case ChannelToken.BluetoothConnection:
                        await _bluetoothConnection!.Value.connection.SendMessageAsync(pongMsg, CancellationToken.None).ConfigureAwait(false);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException($"Somehow created an invalid channel token {token}");
                }
                break;
            case PongMsg:
                // No action needs taken
                break;
            case FileOfferMsg(var fileOffer):
                Console.WriteLine($"File {fileOffer.FileId}  {fileOffer.Name} offered");
                var accept = FileOfferDecision is null || await FileOfferDecision(fileOffer).ConfigureAwait(false);
                if (accept) {
                    bool accepted;
                    lock (_fileTransferStates) {
                        var output =
                            storageService.OpenDownloadFileWriteStream(
                                $"recv_{DateTime.Now:yyyyMMdd_HHmmss}_{SanitizeName(fileOffer.Name)}");
                        if (output is var (stream, outputPath)) {
                            var ableToAdd = _fileTransferStates.TryAdd(fileOffer.FileId,
                                new ReceiveInProgress(stream, outputPath, fileOffer.Size, 0,
                                    IncrementalHash.CreateHash(HashAlgorithmName.SHA256), 0));
                            Console.WriteLine($"Trying to update file recv state {ableToAdd}");
                            accepted = ableToAdd;
                        }
                        else {
                            accepted = false;
                        }
                    }

                    if (accepted) {
                        await SendMessage(new FileAcceptMsg(fileOffer.FileId), _sessionCtSource.Token).ConfigureAwait(false);
                    }
                    else {
                        await SendMessage(new FileRejectMsg(fileOffer.FileId, FileRejectReason.InternalError), _sessionCtSource.Token).ConfigureAwait(false);
                    }
                }
                else {
                    await SendMessage(new FileRejectMsg(fileOffer.FileId, FileRejectReason.UserRejected), _sessionCtSource.Token).ConfigureAwait(false);
                }
                break;
            case FileAcceptMsg(var fileId):
                lock (_fileTransferStates) {
                    _fileTransferStates.TryGetValue(fileId, out var fileAcceptTs);
                    if (fileAcceptTs is AwaitingDecision decisionTs) {
                        // Does not update the transfer states data structure, must do this where the file
                        // sending is occuring as needs the stream and hash

                        decisionTs.DecisionTcs.TrySetResult(true);
                    }
                }

                break;
            case FileRejectMsg(var fileId, var reason):
                lock (_fileTransferStates) {
                    _fileTransferStates.TryGetValue(fileId, out var fileRejectTs);
                    if (fileRejectTs is AwaitingDecision decisionTs) {
                        _fileTransferStates.Remove(fileId);
                        decisionTs.DecisionTcs.TrySetResult(false);
                    }
                    else if (fileRejectTs is AwaitingAck) {
                        // Just remove the file if it has been rejected for now
                        _fileTransferStates.Remove(fileId);
                    }
                }
                break;
            case SwitchConnectionRequest:
                if (TrySwitchConnection(token)) {
                    await SendMessage(new SwitchConnectionAccept(), _sessionCtSource.Token).ConfigureAwait(false);
                }
                else {
                    await SendMessage(new SwitchConnectionReject(), _sessionCtSource.Token).ConfigureAwait(false);
                }
                break;
            case SwitchConnectionAccept:
                // Know the connection shouldn't be null as we've just received a message on them
                _connectionInUse = token switch {
                    ChannelToken.TcpConnection => _tcpConnection!.Value.connection,
                    ChannelToken.BluetoothConnection => _bluetoothConnection!.Value.connection,
                    _ => throw new ArgumentOutOfRangeException($"Somehow matched on non existent enum varient {token}")
                };
                break;
            case SwitchConnectionReject:
                // No action needs taken
                break;
            case DisconnectMsg:
                await _sessionCtSource.CancelAsync();
                if (_tcpConnection is var (tcpConn, _)) {
                    tcpConn.Dispose();
                    _tcpConnection = null;
                }

                if (_bluetoothConnection is var (btConn, _)) {
                    btConn.Dispose();
                    _bluetoothConnection = null;
                }
                _sessionCtSource = new CancellationTokenSource();
                SessionEnded?.Invoke(SessionEndReason.PeerRequested);
                break;
            default: throw new Exception("Unknown message");
        }
    }

    private bool TrySwitchConnection(ChannelToken token) {
        (IConnection, Task)? newConnection;
        switch (token) {
            case ChannelToken.TcpConnection:
                newConnection = _tcpConnection;
                break;
            case ChannelToken.BluetoothConnection:
                newConnection = _bluetoothConnection;
                break;
            default:
                throw new ArgumentOutOfRangeException($"Somehow matched on non existent enum varient {token}");
        }

        if (newConnection?.Item1 is { IsConnected: true }) {
            _connectionInUse = newConnection.Value.Item1;
            return true;
        }
        return false;
    }

    private async Task RespondToDataMessage(ChannelToken token, FileMsg message) {
        switch (message) {
            case FileChunkMsg(var fileId, var chunkIndex, var data):
                FileTransferState? fileTs;
                lock (_fileTransferStates) {
                    _fileTransferStates.TryGetValue(fileId, out fileTs);
                }

                if (fileTs is ReceiveInProgress recvState) {
                    if (chunkIndex != recvState.ExpectedChunkIndex) return;
                    await recvState.SaveStream.WriteAsync(data, _sessionCtSource.Token).ConfigureAwait(false);
                    recvState.ExpectedChunkIndex++;
                    recvState.WrittenBytes += data.Length;
                    recvState.Hash.AppendData(data.Span);
                }
                break;
            case FileDoneMsg(var fileId, var sha256):
                await HandleFileDone(fileId, sha256);
                break;
            case FileAckMsg(var fileId, var _):
                lock (_fileTransferStates) {
                    _fileTransferStates.TryGetValue(fileId, out var fileAckState);
                    // Only remove if it is actually awaiting an ack
                    if (fileAckState is AwaitingAck awaitingAck) {
                        _fileTransferStates.Remove(fileId);
                        FileAcked?.Invoke(fileId, awaitingAck.Hash.ToString()!);
                    }
                }
                break;
            default: throw new Exception("Unknown message");
        }
    }

    private async Task HandleFileDone(Guid fileId, ReadOnlyMemory<byte> sha256) {
        FileTransferState? fileDoneTs;
        lock (_fileTransferStates) {
            _fileTransferStates.TryGetValue(fileId, out fileDoneTs);
        }

        if (fileDoneTs is ReceiveInProgress completedState) {
            await completedState.SaveStream.FlushAsync().ConfigureAwait(false);
            await completedState.SaveStream.DisposeAsync().ConfigureAwait(false);
            var localHash = completedState.Hash.GetHashAndReset();
            completedState.Hash.Dispose();

            if (completedState.WrittenBytes != completedState.ExpectedSizeBytes) {
                await SendMessage(new FileRejectMsg(fileId, FileRejectReason.SizeMismatch), _sessionCtSource.Token).ConfigureAwait(false);
                // DELETE FILE PROPERLY
                return;
            }

            if (!CryptographicOperations.FixedTimeEquals(localHash, sha256.Span)) {
                await SendMessage(new FileRejectMsg(fileId, FileRejectReason.HashMismatch), _sessionCtSource.Token).ConfigureAwait(false);
                //Delete file properly
                return;
            }

            FileSaved?.Invoke(completedState.SavePath);
            await SendMessage(new FileAckMsg(fileId, sha256), _sessionCtSource.Token).ConfigureAwait(false);
        }
    }

    private static string SanitizeName(string name) {
        return Path.GetInvalidFileNameChars().Aggregate(name, (current, c) => current.Replace(c, '_'));
    }

    public void Dispose() {
        _tcpConnection?.connection.Dispose();
        _bluetoothConnection?.connection.Dispose();
    }

    public enum SessionEndReason {
        AllChannelsDisconnected,
        PeerRequested
    }
}
