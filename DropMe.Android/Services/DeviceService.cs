using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DropMe.Services;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;

namespace DropMe.Android.Services;

public class DeviceService : IDeviceService {
    public string GetDeviceString() {
        return "Android";
    }

    public string GetLocalLanIp() {
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces()) {
            if (ni.OperationalStatus != OperationalStatus.Up)
                continue;

            var ipProps = ni.GetIPProperties();
            var addr = ipProps.UnicastAddresses
                .FirstOrDefault(a =>
                    a.Address.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(a.Address));

            if (addr is not null)
                return addr.Address.ToString();
        }

        return "0.0.0.0";
    }

    public (BluetoothAddress? address, string name)? GetLocalBluetoothInfo() {
        var radio = BluetoothRadio.Default;
        // Bluetooth unavailable
        if (radio == null) return null;
        var name = radio.Name;
        return (null, name);
    }
}