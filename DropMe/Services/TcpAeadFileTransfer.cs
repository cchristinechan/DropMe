using System;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
namespace DropMe.Services;

public sealed class TcpAeadFileTransfer : IFileTransfer {
    //                                                   Drop       Me         File       Transfer
    private static readonly byte[] Magic = new byte[] { (byte)'D', (byte)'M', (byte)'F', (byte)'T' };
    private const byte Version = 1;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public async Task SendFileEncryptedAsync(
        string sourceFile,
        IPEndPoint dest,
        byte[] key,
        uint chunkSize = 64 * 1024,
        CancellationToken ct = default) {
        if (key is null) throw new ArgumentNullException(nameof(key));
        if (chunkSize == 0) throw new ArgumentOutOfRangeException(nameof(chunkSize));
        var fileInfo = new FileInfo(sourceFile);
        long fileSize = fileInfo.Length;
        string fileName = fileInfo.Name;

        using var client = new TcpClient();
        await client.ConnectAsync(dest.Address, dest.Port, ct).ConfigureAwait(false);
        await using var net = client.GetStream();
        await using var file = File.OpenRead(sourceFile);
        await using var fileStream = new BufferedStream(file);

        // 12 byte base nonce
        var baseNonce = new byte[NonceSize];
        RandomNumberGenerator.Fill(baseNonce);
        // Write header and keep exact bytes for authenticated associated data (header) AAD
        byte[] headerBytes = await WriteHeaderAsync(net, new FileTransferHeader(
            ChunkSize: chunkSize,
            FileSize: fileSize,
            FileName: fileName,
            Encrypted: true,
            BaseNonce: baseNonce
        ), ct).ConfigureAwait(false);

        using var gcm = new AesGcm(key, TagSize);
        byte[] plain = new byte[chunkSize];
        byte[] cipher = new byte[chunkSize];
        byte[] tag = new byte[TagSize];
        byte[] nonce = new byte[NonceSize];

        long sent = 0;
        uint counter = 0;

        while (sent < fileSize) {
            int toRead = (int)Math.Min((long)chunkSize, fileSize - sent);
            int read = 0;
            while (read < toRead) {
                int r = await fileStream.ReadAsync(plain.AsMemory(read, toRead - read), ct).ConfigureAwait(false);
                if (r == 0) throw new EndOfStreamException("Unexpected EOF while reading source file.");
                read += r;
            }

            // baseNonce with last 4 bytes = counter (LE)
            Buffer.BlockCopy(baseNonce, 0, nonce, 0, NonceSize);
            BinaryPrimitives.WriteUInt32LittleEndian(nonce.AsSpan(NonceSize - 4, 4), counter);

            gcm.Encrypt(nonce, plain.AsSpan(0, read), cipher.AsSpan(0, read), tag, headerBytes);

            await net.WriteUInt32LEAsync((uint)read, ct).ConfigureAwait(false);
            await net.WriteAsync(cipher.AsMemory(0, read), ct).ConfigureAwait(false);
            await net.WriteAsync(tag.AsMemory(0, TagSize), ct).ConfigureAwait(false);

            sent += read;
            counter++;
        }
        await net.FlushAsync(ct).ConfigureAwait(false);
    }

    public async Task<FileTransferHeader> ReceiveFileEncryptedAsync(
        IPEndPoint listenEndPoint,
        string destFile,
        byte[] key,
        CancellationToken ct = default) {
        if (key is null) throw new ArgumentNullException(nameof(key));

        var listener = new TcpListener(listenEndPoint);
        listener.Start();

        try {
            using var client = await listener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
            await using var net = client.GetStream();
            await using var file = File.Create(destFile);
            await using var fileStream = new BufferedStream(file);
            
            (FileTransferHeader header, byte[] headerBytes) = await ReadHeaderAsync(net, ct).ConfigureAwait(false);
            if (!header.Encrypted)
                throw new InvalidOperationException("Expected encrypted transfer but header.Encrypted was false.");
            if (header.BaseNonce is null || header.BaseNonce.Length != NonceSize)
                throw new InvalidOperationException("Missing/invalid BaseNonce in header.");

            using var gcm = new AesGcm(key, TagSize);
            byte[] cipher = new byte[header.ChunkSize];
            byte[] plain = new byte[header.ChunkSize];
            byte[] tag = new byte[TagSize];
            byte[] nonce = new byte[NonceSize];

            long written = 0;
            uint counter = 0;

            while (written < header.FileSize) {
                uint plainLenU = await net.ReadUInt32LEAsync(ct).ConfigureAwait(false);
                if (plainLenU == 0 || plainLenU > header.ChunkSize)
                    throw new InvalidDataException($"Invalid chunk length {plainLenU}.");
                int plainLen = (int)plainLenU;

                await net.ReadExactlyAsync(cipher, 0, plainLen, ct).ConfigureAwait(false);
                await net.ReadExactlyAsync(tag, 0, TagSize, ct).ConfigureAwait(false);
                Buffer.BlockCopy(header.BaseNonce, 0, nonce, 0, NonceSize);
                BinaryPrimitives.WriteUInt32LittleEndian(nonce.AsSpan(NonceSize - 4, 4), counter);

                gcm.Decrypt(nonce, cipher.AsSpan(0, plainLen), tag, plain.AsSpan(0, plainLen), headerBytes);
                await fileStream.WriteAsync(plain.AsMemory(0, plainLen), ct).ConfigureAwait(false);
                
                written += plainLen;
                counter++;
            }
            await fileStream.FlushAsync(ct).ConfigureAwait(false);
            return header;
        }
        finally {
            listener.Stop();
        }
    }

    private static async Task<byte[]> WriteHeaderAsync(Stream net, FileTransferHeader header, CancellationToken ct) {
        byte flags = 0;
        if (header.Encrypted) flags |= 0b0000_0001;

        byte[] nameBytes = StreamIoExtensions.Utf8(header.FileName);
        if (nameBytes.Length > ushort.MaxValue) throw new ArgumentOutOfRangeException(nameof(header.FileName), "File name too long.");

        // Construct header bytes in memory to use them as AAD
        using var ms = new MemoryStream();
        await ms.WriteAsync(Magic, ct).ConfigureAwait(false);
        ms.WriteByte(Version);
        ms.WriteByte(flags);
        await ms.WriteUInt32LEAsync(header.ChunkSize, ct).ConfigureAwait(false);
        await ms.WriteInt64LEAsync(header.FileSize, ct).ConfigureAwait(false);
        await ms.WriteUInt16LEAsync((ushort)nameBytes.Length, ct).ConfigureAwait(false);
        await ms.WriteAsync(nameBytes, ct).ConfigureAwait(false);

        if (header.Encrypted) {
            if (header.BaseNonce is null || header.BaseNonce.Length != NonceSize)
                throw new InvalidOperationException("Encrypted header requires a 12-byte BaseNonce.");
            await ms.WriteAsync(header.BaseNonce, ct).ConfigureAwait(false);
        }

        byte[] headerBytes = ms.ToArray();
        await net.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        await net.FlushAsync(ct).ConfigureAwait(false);
        return headerBytes;
    }

    private static async Task<(FileTransferHeader header, byte[] headerBytes)> ReadHeaderAsync(Stream net, CancellationToken ct) {
        // Read fixed part first to know name length + flags
        var fixedPrefix = new byte[4 + 1 + 1 + 4 + 8 + 2]; // magic + ver + flags + chunk + size + nameLen
        await net.ReadExactlyAsync(fixedPrefix, 0, fixedPrefix.Length, ct).ConfigureAwait(false);

        if (fixedPrefix[0] != Magic[0] || fixedPrefix[1] != Magic[1] || fixedPrefix[2] != Magic[2] || fixedPrefix[3] != Magic[3])
            throw new InvalidDataException("Bad magic.");
        byte ver = fixedPrefix[4];
        if (ver != Version) throw new InvalidDataException($"Unsupported version: {ver}.");

        byte flags = fixedPrefix[5];
        bool encrypted = (flags & 0b0000_0001) != 0;

        uint chunkSize = BinaryPrimitives.ReadUInt32LittleEndian(fixedPrefix.AsSpan(6, 4));
        long fileSize = BinaryPrimitives.ReadInt64LittleEndian(fixedPrefix.AsSpan(10, 8));
        ushort nameLen = BinaryPrimitives.ReadUInt16LittleEndian(fixedPrefix.AsSpan(18, 2));

        var nameBytes = new byte[nameLen];
        await net.ReadExactlyAsync(nameBytes, 0, nameBytes.Length, ct).ConfigureAwait(false);
        string fileName = System.Text.Encoding.UTF8.GetString(nameBytes);

        byte[]? baseNonce = null;
        if (encrypted) {
            baseNonce = new byte[NonceSize];
            await net.ReadExactlyAsync(baseNonce, 0, NonceSize, ct).ConfigureAwait(false);
        }

        // Reconstruct header bytes (fixedPrefix + nameBytes + optional nonce) for AAD
        byte[] headerBytes;
        if (encrypted) {
            headerBytes = new byte[fixedPrefix.Length + nameBytes.Length + NonceSize];
            Buffer.BlockCopy(fixedPrefix, 0, headerBytes, 0, fixedPrefix.Length);
            Buffer.BlockCopy(nameBytes, 0, headerBytes, fixedPrefix.Length, nameBytes.Length);
            Buffer.BlockCopy(baseNonce!, 0, headerBytes, fixedPrefix.Length + nameBytes.Length, NonceSize);
        }
        else {
            headerBytes = new byte[fixedPrefix.Length + nameBytes.Length];
            Buffer.BlockCopy(fixedPrefix, 0, headerBytes, 0, fixedPrefix.Length);
            Buffer.BlockCopy(nameBytes, 0, headerBytes, fixedPrefix.Length, nameBytes.Length);
        }
        return (new FileTransferHeader(chunkSize, fileSize, fileName, encrypted, baseNonce), headerBytes);
    }
}
