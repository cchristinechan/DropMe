using InTheHand.Net;

namespace DropMe.Services;

public interface IDeviceService {
    public string GetDeviceString();
    public string GetLocalLanIp();
    public (BluetoothAddress? address, string name)? GetLocalBluetoothInfo();
}