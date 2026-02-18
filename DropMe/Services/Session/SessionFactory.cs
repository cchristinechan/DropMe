using System.Net;

namespace DropMe.Services.Session;

public sealed class SessionFactory {
    public ISession Create(IStorageService storageService, ConnectionInvite invite) {
        var ep = new IPEndPoint(
            IPAddress.Parse(invite.Ip),
            invite.Port);

        return new TcpAesGcmSession(storageService, ep);
    }
}