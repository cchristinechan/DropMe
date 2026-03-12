using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services.Session;

public interface ISession {
    SessionState State { get; }
    string Peer { get; }

    public event Action<string>? FileSaved;
    public event Action<Guid, string /*sha256 hex*/>? FileAcked;

    public Func<FileOfferInfo, Task<bool>>? FileOfferDecision { get; set; }

    Task Connect(CancellationToken ct);
    Task StartAsync(CancellationToken ct);
    Task SendFileAsync(Stream file, string filename, CancellationToken ct);
    Task StopAsync();
}