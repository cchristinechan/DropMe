using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services.Session;

public interface ISession {
    SessionState State { get; }
    string Peer { get; }

    Task StartAsync(CancellationToken ct);
    Task SendFileAsync(Stream file, string filename, CancellationToken ct);
    Task StopAsync();
}