using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services.Session;

public interface ISession {
    SessionState State { get; }
    string Peer { get; }

    Task StartAsync(CancellationToken ct);
    Task SendFileAsync(string path, CancellationToken ct);
    Task StopAsync();
}