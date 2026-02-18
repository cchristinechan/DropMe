using System.Net;

namespace DropMe.Services.Session;

public sealed class SessionFactory {
    public ISession Create(ConnectionInvite invite) {
        var ep = new IPEndPoint(
            IPAddress.Parse(invite.Ip),
            invite.Port);

        return new TcpAesGcmSession(ep);
    }
}