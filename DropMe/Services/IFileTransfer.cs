using System.Net;
using System.Threading;
using System.Threading.Tasks;
namespace DropMe.Services;

public interface IFileTransfer {
    Task SendFileEncryptedAsync(
        string sourceFile,
        IPEndPoint dest,
        byte[] key,
        uint chunkSize = 64 * 1024,
        CancellationToken ct = default);

    // Receive file and write to destFile.
    Task<FileTransferHeader> ReceiveFileEncryptedAsync(
        IPEndPoint listenEndPoint,
        string destFile,
        byte[] key,
        CancellationToken ct = default);
}