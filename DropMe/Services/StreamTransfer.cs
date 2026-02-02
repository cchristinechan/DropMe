using System.IO;
using System.Threading;
using System.Threading.Tasks;
namespace DropMe.Services;

public static class StreamTransfer {
    // Sync PoC
    public static void SendStream(Stream source, Stream dest) {
        // Copies until EOF (source.Read returns 0)
        source.CopyTo(dest);
        dest.Flush();
    }

    public static void RecvStream(Stream source, Stream dest) {
        source.CopyTo(dest);
        dest.Flush();
    }

    // Async version (needed for networking)
    public static async Task SendStreamAsync(Stream source, Stream dest, CancellationToken ct = default) {
        await source.CopyToAsync(dest, 81920, ct).ConfigureAwait(false);
        await dest.FlushAsync(ct).ConfigureAwait(false);
    }

    public static async Task RecvStreamAsync(Stream source, Stream dest, CancellationToken ct = default) {
        await source.CopyToAsync(dest, 81920, ct).ConfigureAwait(false);
        await dest.FlushAsync(ct).ConfigureAwait(false);
    }
}