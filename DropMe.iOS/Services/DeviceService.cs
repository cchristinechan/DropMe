using DropMe.Services;

namespace DropMe.iOS.Services;

public class DeviceService : IDeviceService {
    public string GetDeviceString() {
        return "IOS";
    }
}