using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
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
        if (!IsDisposed) {
            IsDisposed = true;
            client.Close();
        }
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
    // It's fine for there to be a reader and a writer thread, but there could potentially be two writers
    // so need to lock on writes
    private readonly SemaphoreSlim _streamWriteSemaphore = new(1, 1);
    private uint _writeSeq = 0; // Protected by _streamWriteSemaphore
    private uint _nextReadSeq = 0;
    private bool _handshakePerformed = false;
    private const int PingIntervalSeconds = 2;
    private const int NonceLength = 12;
    private const int TagSizeBytes = 16;
    private const int HandshakeChallengeLengthBytes = 16;

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
        Debug.Assert(!_handshakePerformed, "Handshake already performed");

        // Receive and respond to server's challenge
        var serverChallenge = new byte[HandshakeChallengeLengthBytes];
        await streamProvider.NStream.ReadExactlyAsync(serverChallenge, 0, HandshakeChallengeLengthBytes, ct)
            .ConfigureAwait(false);
        await SendEncryptedDataAsync(serverChallenge, ct).ConfigureAwait(false);

        // Give server our own challenge to ensure it has the key too
        try {
            var clientChallenge = RandomNumberGenerator.GetBytes(HandshakeChallengeLengthBytes);
            await streamProvider.NStream.WriteAsync(clientChallenge, ct).ConfigureAwait(false);
            var serverResponse = await ReceiveEncryptedDataAsync(ct).ConfigureAwait(false);
            if (serverResponse.SequenceEqual(clientChallenge)) {
                // Send server a message letting it know the connection is open
                Console.WriteLine("Opening connection");
                await SendMessageAsync(new PingMsg(), ct).ConfigureAwait(false);
                _handshakePerformed = true;
                return true;
            }
            Console.WriteLine("Server response to client challenge incorrect");
            streamProvider.NStream.Close(0);
            // Incorrect response
            return false;
        }
        catch (Exception e) {
            // Presumably the server's not happy with the response and has closed the connection
            Console.WriteLine("Server closed connection");
            return false;
        }
    }

    public async Task<bool> ServerConnectionHandshake(CancellationToken ct) {
        Debug.Assert(!_handshakePerformed, "Handshake already performed");
        var serverChallenge = RandomNumberGenerator.GetBytes(HandshakeChallengeLengthBytes);
        // No need to aquire semaphore as this must be done before anything else, no other tasks can be running
        // using the write stream
        await streamProvider.NStream.WriteAsync(serverChallenge, ct).ConfigureAwait(false);
        var clientResponse = await ReceiveEncryptedDataAsync(ct).ConfigureAwait(false);
        if (!clientResponse.SequenceEqual(serverChallenge)) {
            // If client didn't respond correctly, kill the connection
            streamProvider.NStream.Close(0);
            return false;
        }
        var clientChallenge = new byte[HandshakeChallengeLengthBytes];
        await streamProvider.NStream.ReadExactlyAsync(clientChallenge, 0, HandshakeChallengeLengthBytes, ct)
            .ConfigureAwait(false);
        await SendEncryptedDataAsync(clientChallenge, ct).ConfigureAwait(false);

        try {
            Console.WriteLine("Trying to receive open message");
            var data = await ReceiveEncryptedDataAsync(ct);
            var message = MessageFraming.ParseMessage(data);
            if (message is PingMsg) {
                _handshakePerformed = true;
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
        var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var kaLoop = Task.Run(() => KeepAliveLoop(ct), cts.Token);
        while (true) {
            try {
                var message = await ReceiveMessageAsync(ct);
                await channel.WriteAsync((this, message), ct).ConfigureAwait(false);
            }
            catch (InvalidDataException e) {
                // Allow session to continue with the message dropped if the message is invalid
                Console.WriteLine(e.Message);
            }
            catch (InvalidOperationException e) {
                // The channel has been closed and the caller's no longer accepting messages
                // so quit after cancelling the keep alive loop
                await cts.CancelAsync().ConfigureAwait(false);
                await kaLoop.ConfigureAwait(false);
                return;
            }
            catch (Exception e) {
                Console.WriteLine("Error receiving message");
                streamProvider.Dispose();
                return;
            }
        }
    }

    public async Task SendMessageAsync(DropMeMsg msg, CancellationToken ct) {
        var (header, body) = MessageFraming.FrameMessage(msg);
        var combined = new byte[header.Length + body.Length];
        Buffer.BlockCopy(header, 0, combined, 0, header.Length);
        Buffer.BlockCopy(body, 0, combined, header.Length, body.Length);
        if (!IsConnected)
            throw new Exception("Not connected");
        await SendEncryptedDataAsync(combined, ct).ConfigureAwait(false);
    }

    private async Task<DropMeMsg> ReceiveMessageAsync(CancellationToken ct) {
        var data = await ReceiveEncryptedDataAsync(ct);
        return MessageFraming.ParseMessage(data);
    }

    private async Task SendEncryptedDataAsync(byte[] data, CancellationToken ct) {
        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var cipherText = new byte[data.Length];
        var tag = new byte[TagSizeBytes];

        var totalLength = 4 + nonce.Length + cipherText.Length + tag.Length;

        var lengthBytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthBytes, totalLength);

        var seqBytes = new byte[4];

        await _streamWriteSemaphore.WaitAsync(ct);
        try {
            BinaryPrimitives.WriteUInt32BigEndian(seqBytes, _writeSeq);
            _writeSeq++;

            using var aes = new AesGcm(aesKey, TagSizeBytes);
            aes.Encrypt(nonce, data, cipherText, tag, seqBytes);

            await streamProvider.NStream.WriteAsync(lengthBytes, ct).ConfigureAwait(false);
            await streamProvider.NStream.WriteAsync(seqBytes, ct).ConfigureAwait(false);
            await streamProvider.NStream.WriteAsync(nonce, ct).ConfigureAwait(false);
            await streamProvider.NStream.WriteAsync(tag, ct).ConfigureAwait(false);
            await streamProvider.NStream.WriteAsync(cipherText, ct).ConfigureAwait(false);
        }
        finally {
            _streamWriteSemaphore.Release();
        }
    }

    private async Task<byte[]> ReceiveEncryptedDataAsync(CancellationToken ct) {
        var lengthBytes = new byte[4];
        await streamProvider.NStream.ReadExactlyAsync(lengthBytes, ct);
        var length = BinaryPrimitives.ReadInt32BigEndian(lengthBytes);

        var buffer = new byte[length];
        await streamProvider.NStream.ReadExactlyAsync(buffer, ct);

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

    private async Task KeepAliveLoop(CancellationToken ct) {
        while (!ct.IsCancellationRequested && IsConnected) {
            try {
                await SendMessageAsync(new PingMsg(), ct);
                await Task.Delay(TimeSpan.FromSeconds(PingIntervalSeconds), ct).ConfigureAwait(false);
            }
            catch (Exception e) {
                Console.WriteLine($"Keep alive loop exception {e.Message}, closing connection");
                streamProvider.Dispose();
            }
        }
    }

    public void Dispose() {
        streamProvider.Dispose();
    }
}