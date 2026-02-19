using DropMe.Services;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace DropMe.iOS.Services;

public class DeviceService : IDeviceService {
    public string GetDeviceString() {
        return "IOS";
    }

    public string GetLocalLanIp() {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;

            var props = ni.GetIPProperties();
            foreach (var ua in props.UnicastAddresses) {
                if (ua.Address.AddressFamily == AddressFamily.InterNetwork) {
                    var ip = ua.Address.ToString();
                    if (!ip.StartsWith("169.254.") && ip != "127.0.0.1")
                        return ip;
                }
            }
        }

        return "127.0.0.1";
    }
}
