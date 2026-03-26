using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using InTheHand.Net.Sockets;

namespace DropMe.Services.Session;

public interface INetworkStreamProvider {
    public NetworkStream NStream { get; }

    public bool IsDisposed { get; }
}

public class TcpClientNsAdapter(TcpClient client) : INetworkStreamProvider, IDisposable {
    public NetworkStream NStream => client.GetStream();
    public bool IsDisposed { get; private set; } = false;

    public void Dispose() {
        if (!IsDisposed) {
            IsDisposed = true;
            client.Close();
        }
    }
}

public class BluetoothClientNsAdapter(BluetoothClient client) : INetworkStreamProvider, IDisposable {
    public NetworkStream NStream => client.GetStream();
    public bool IsDisposed { get; private set; } = false;

    public void Dispose() {
        Console.WriteLine("Bluetooth ns adapter disposed");
        if (!IsDisposed) {
            IsDisposed = true;
            client.Close();
        }
    }
}

internal class TimeoutTimer(TimeSpan interval) {
    private CancellationTokenSource _cts = new();

    public async Task StartAsync(Func<Task> action) {
        while (true) {
            var cts = _cts;
            try {
                await Task.Delay(interval, cts.Token);
                Console.WriteLine("Timeout expired");
                await action();
                break; // If timed out then stop
            }
            catch (TaskCanceledException) {
            }
        }
    }

    public void Reset() {
        // Cancel the current timer and create a new one
        _cts.Cancel();
        _cts = new CancellationTokenSource();
    }
}

public interface IConnection : IDisposable {
    public bool IsConnected { get; }
    public string PeerName { get; }

    public Task StartHandlingMessages(ChannelWriter<(IConnection, DropMeMsg)> channel, CancellationToken ct);
    public Task SendMessageAsync(DropMeMsg msg, CancellationToken ct);
}

public class EncryptedConnection<T>(T streamProvider, string peerName, byte[] aesKey) : IConnection where T : INetworkStreamProvider, IDisposable {
    public bool IsConnected => !streamProvider.IsDisposed && streamProvider.NStream is { Socket.Connected: true };
    public string PeerName => peerName;
    public event Action<IConnection, IEnumerable<DropMeMsg>>? OnDisconnect;
    // It's fine for there to be a reader and a writer thread, but there could potentially be two writers
    // so need to lock on writes
    private readonly SemaphoreSlim _streamWriteSemaphore = new(1, 1);
    private uint _writeSeq = 0; // Protected by _streamWriteSemaphore
    private readonly Queue<(uint seq, DropMeMsg msg)> _sendQueue = new(); // Protected by _streamWriteSemaphore
    private uint _nextReadSeq = 0;
    private bool _handshakePerformed = false;
    private const int PingIntervalSeconds = 5;
    private const int TimeoutSeconds = PingIntervalSeconds * 2;
    private const int NonceLength = 12;
    private const int TagSizeBytes = 16;
    private const int HandshakeChallengeLengthBytes = 16;
    private readonly TimeoutTimer _timeoutTimer = new(new TimeSpan(0, 0, TimeoutSeconds));
    private readonly CancellationTokenSource _disconnectedCts = new();
    private int _disconnected = 0;

    // Handshake protocol:
    // 1. Server sends client random bytes
    // 2. Client sends server these bytes encrypted then its own challenge bytes
    // 3. If the client has successfully encrypted them it encrypts and sends back the client's challenge
    // 4. If the server has successfully encrypted the client's challenge it sends a ping message to indicate an open connection
    // From there everything should be sent using SendMessageAsync and received using ReceiveMessageAsync as this message framing
    // is expected.
    // Message 4 could likely be removed but it's currently there to make it easier to figure out if the connection is successful
    // and I haven't noticed much latency being added from it.
    // This verifies that both the client and server have the same encryption key, and therefore agree on the QR code.
    // Handshake failing should be used to indicate the peer is wrong, e.g. bluetooth service discovery found 2 dropme capable servers with the same name
    // and unknown MAC addresses, and that the next server should be tried

    public async Task<bool> ClientConnectionHandshake(CancellationToken ct) {
        //Debug.Assert(!_handshakePerformed, "Handshake already performed");

        Console.WriteLine("Waiting for server challenge");
        var serverChallenge = await ReadExactlyAsync(HandshakeChallengeLengthBytes, ct).ConfigureAwait(false);
        Console.WriteLine("Read challenge from server");
        await SendEncryptedDataAsync(serverChallenge, ct);
        Console.WriteLine($"Sent response to server");

        // Give server our own challenge to ensure it has the key too
        try {
            var clientChallenge = RandomNumberGenerator.GetBytes(HandshakeChallengeLengthBytes);
            //await streamProvider.NStream.WriteAsync(clientChallenge, ct).ConfigureAwait(false);
            await WriteAsync(clientChallenge, ct);
            var serverResponse = await ReceiveEncryptedDataAsync(ct).ConfigureAwait(false);
            if (serverResponse.SequenceEqual(clientChallenge)) {
                // Send server a message letting it know the connection is open
                Console.WriteLine("Opening connection");
                await SendMessageAsync(new PingMsg(), ct);
                _handshakePerformed = true;
                var _ = _timeoutTimer.StartAsync(OnConnectionDead);
                return true;
            }
            Console.WriteLine("Server response to client challenge incorrect");
            streamProvider.NStream.Close(0);
            // Incorrect response
            return false;
        }
        catch (Exception) {
            // Presumably the server's not happy with the response and has closed the connection
            Console.WriteLine("Server closed connection");
            return false;
        }
    }

    public async Task<bool> ServerConnectionHandshake(CancellationToken ct) {
        Console.WriteLine($"Nodelay was {streamProvider.NStream.Socket.NoDelay} sendbuf {streamProvider.NStream.Socket.SendBufferSize}");
        Debug.Assert(!_handshakePerformed, "Handshake already performed");
        var serverChallenge = RandomNumberGenerator.GetBytes(HandshakeChallengeLengthBytes);
        Console.WriteLine("Challenge: " + String.Join(", ", serverChallenge));
        // No need to aquire semaphore as this must be done before anything else, no other tasks can be running
        // using the write stream
        Console.WriteLine("Writing challenge to client");
        await WriteAsync(serverChallenge, ct);
        Console.WriteLine("Written");

        var clientResponse = await ReceiveEncryptedDataAsync(ct);

        Console.WriteLine("Received response from client");
        if (!clientResponse.SequenceEqual(serverChallenge)) {
            // If client didn't respond correctly, kill the connection
            streamProvider.NStream.Close(0);
            return false;
        }
        var clientChallenge = await ReadExactlyAsync(HandshakeChallengeLengthBytes, ct).ConfigureAwait(false);
        await SendEncryptedDataAsync(clientChallenge, ct);

        try {
            Console.WriteLine("Trying to receive open message");
            var data = await ReceiveEncryptedDataAsync(ct);
            var (message, _) = MessageFraming.ParseMessage(data);
            if (message is PingMsg) {
                _handshakePerformed = true;
                var _ = _timeoutTimer.StartAsync(OnConnectionDead);
                return true;
            }

            return false;
        }
        catch (Exception e) {
            // Client wans't happy with the response
            Console.WriteLine("Client killed connection");
            return false;
        }
    }

    public async Task StartHandlingMessages(ChannelWriter<(IConnection, DropMeMsg)> channel, CancellationToken ct) {
        Debug.Assert(_handshakePerformed, "Cannot start handling messages before handshake is performed");
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct, _disconnectedCts.Token);
        var kaLoop = Task.Run(() => KeepAliveLoop(ct), cts.Token).ConfigureAwait(false);
        while (true) {
            try {
                var message = await ReceiveMessageAsync(cts.Token).ConfigureAwait(false);
                await channel.WriteAsync((this, message), cts.Token).ConfigureAwait(false);
            }
            catch (InvalidDataException e) {
                // Allow session to continue with the message dropped if the message is invalid
                Console.WriteLine(e.Message);
            }
            catch (Exception e) {
                Console.WriteLine($"Killing connection. Error receiving messages : {e}");
                await OnConnectionDead().ConfigureAwait(false);
                return;
            }
        }
    }

    public void SendMessage(DropMeMsg msg) {
        Console.WriteLine($"Sending message {msg}");
        var (header, body) = MessageFraming.FrameMessage(msg, _nextReadSeq); // Acknowledge everything we've seen so far
        _sendQueue.Enqueue((_writeSeq, msg));
        var combined = new byte[header.Length + body.Length];
        Buffer.BlockCopy(header, 0, combined, 0, header.Length);
        Buffer.BlockCopy(body, 0, combined, header.Length, body.Length);
        if (!IsConnected)
            throw new InvalidOperationException("Not connected");
        SendEncryptedData(combined);
    }

    public Task SendMessageAsync(DropMeMsg msg, CancellationToken ct) => Task.Run(() => SendMessage(msg), ct);

    private async Task<DropMeMsg> ReceiveMessageAsync(CancellationToken ct) {
        var data = await ReceiveEncryptedDataAsync(ct).ConfigureAwait(false);
        _timeoutTimer.Reset();
        var (msg, ack) = MessageFraming.ParseMessage(data);
        await _streamWriteSemaphore.WaitAsync(ct);
        while (_sendQueue.Count > 0) {
            var (seq, _) = _sendQueue.Peek();
            if (seq < ack) {
                _sendQueue.Dequeue();
            }
            else {
                break;
            }
        }
        _streamWriteSemaphore.Release();
        Console.WriteLine($"Received {msg}");
        return msg;
    }

    private void SendEncryptedData(byte[] data) {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var cipherText = new byte[data.Length];
        var tag = new byte[TagSizeBytes];

        var totalLength = 4 + nonce.Length + cipherText.Length + tag.Length;

        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, totalLength);

        var seqBytes = new byte[4];

        _streamWriteSemaphore.Wait();
        try {
            BinaryPrimitives.WriteUInt32BigEndian(seqBytes, _writeSeq);
            _writeSeq++;

            using var aes = new AesGcm(aesKey, TagSizeBytes);
            aes.Encrypt(nonce, data, cipherText, tag, seqBytes);

            int offset = 0;
            var total = new byte[lengthBytes.Length + seqBytes.Length + nonce.Length + tag.Length + cipherText.Length];
            Buffer.BlockCopy(lengthBytes, 0, total, offset, lengthBytes.Length);
            offset += lengthBytes.Length;
            Buffer.BlockCopy(seqBytes, 0, total, offset, seqBytes.Length);
            offset += seqBytes.Length;
            Buffer.BlockCopy(nonce, 0, total, offset, nonce.Length);
            offset += nonce.Length;
            Buffer.BlockCopy(tag, 0, total, offset, tag.Length);
            offset += tag.Length;
            Buffer.BlockCopy(cipherText, 0, total, offset, cipherText.Length);

            Write(total);
        }
        finally {
            _streamWriteSemaphore.Release();
        }
    }

    private Task SendEncryptedDataAsync(byte[] data, CancellationToken ct) =>
        Task.Run(() => SendEncryptedData(data), ct);

    private byte[] ReceiveEncryptedData() {
        var lengthBytes = ReadExactly(4);
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);

        var buffer = ReadExactly(length);

        var seqBytes = buffer.AsSpan()[0..4];
        var nonce = buffer.AsSpan()[4..16];
        var tag = buffer.AsSpan()[16..32];
        var ciphertext = buffer.AsSpan()[32..];

        var plaintext = new byte[ciphertext.Length];
        var sequence = BinaryPrimitives.ReadUInt32BigEndian(seqBytes);
        if (sequence != _nextReadSeq) {
            Console.WriteLine("Out of seq recv");
            throw new Exception("Received messages out of sequence");
        }

        _nextReadSeq++;

        using var aes = new AesGcm(aesKey, TagSizeBytes);
        try {
            aes.Decrypt(nonce, ciphertext, tag, plaintext, seqBytes);
            return plaintext;

        }
        catch (Exception e) {
            Console.WriteLine($"AES Decryption exception {e.Message}");
            throw;
        }
    }

    private Task<byte[]> ReceiveEncryptedDataAsync(CancellationToken ct) =>
        Task.Run(ReceiveEncryptedData, ct);

    private Task<byte[]> ReadExactlyAsync(int count, CancellationToken ct) => Task.Run(() => ReadExactly(count), ct);

    private byte[] ReadExactly(int count) {
        const int retryDelayMs = 100;
        var read = false;
        var buffer = new byte[count];
        while (!read) {
            try {
                streamProvider.NStream.ReadExactly(buffer, 0, count);
                read = true;
            }
            catch (SocketException e) {
                if (e.ErrorCode != 11) // 11 is currently busy
                    throw;
                Thread.Sleep(retryDelayMs);
            }
        }

        return buffer;
    }

    private Task WriteAsync(byte[] data, CancellationToken ct) => Task.Run(() => Write(data), ct);

    private void Write(byte[] data) {
        const int retryDelayMs = 100;
        var written = false;
        while (!written) {
            try {
                streamProvider.NStream.Write(data, 0, data.Length);
                streamProvider.NStream.Flush();
                written = true;
            }
            catch (SocketException e) {
                if (e.ErrorCode != 11) // 11 is currently busy
                    throw;
                Thread.Sleep(retryDelayMs);
            }
        }
    }

    private async Task OnConnectionDead() {
        Console.WriteLine($"OnConnectionDead called:\n{new System.Diagnostics.StackTrace()}");
        await _disconnectedCts.CancelAsync();
        if (!streamProvider.IsDisposed)
            streamProvider.Dispose();
        if (Interlocked.CompareExchange(ref _disconnected, 1, 0) == 0) {
            Console.WriteLine("Wasn't previously disconnected");
            await _streamWriteSemaphore.WaitAsync(CancellationToken.None);
            OnDisconnect?.Invoke(this, _sendQueue.Select(val => val.msg));
            _streamWriteSemaphore.Release();
        }
    }

    private async Task KeepAliveLoop(CancellationToken ct) {
        while (!ct.IsCancellationRequested && IsConnected) {
            try {
                await SendMessageAsync(new PingMsg(), ct).ConfigureAwait(false);
                await Task.Delay(TimeSpan.FromSeconds(PingIntervalSeconds), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) {
                Console.WriteLine($"Keep alive loop cancelled, killing connection if not already dead");
                await OnConnectionDead();
            }
            catch (Exception e) {
                Console.WriteLine($"Keep alive loop exception {e.Message}, closing connection");
                await OnConnectionDead();
            }
        }
    }

    public void Dispose() {
        Console.WriteLine("Disposing connection");
        if (!streamProvider.IsDisposed)
            streamProvider.Dispose();
    }
}