using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using DropMe.Services;
using InTheHand.Net;
using InTheHand.Net.Bluetooth;

namespace DropMe.Desktop.Services;

public sealed class DeviceService : IDeviceService {
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

                    // Skip APIPA / localhost
                    if (!ip.StartsWith("169.254.") && ip != "127.0.0.1")
                        return ip;
                }
            }
        }

        // Fallback (still better than crashing)
        return "127.0.0.1";
    }

    public (BluetoothAddress? address, string name)? GetLocalBluetoothInfo() {
        // Supported bluetooth platforms
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux()) {
            try {
                var radio = BluetoothRadio.Default;
                // Bluetooth unavailable
                if (radio == null || radio.Mode == RadioMode.PowerOff) return null;

                var radioName = string.IsNullOrWhiteSpace(radio.Name)
                    ? Environment.MachineName
                    : radio.Name;
                return (radio.LocalAddress, radioName);
            }
            catch (Exception ex) {
                Console.WriteLine($"Desktop Bluetooth probe failed: {ex.Message}");
                return null;
            }
        }

        return null;
    }
}
