using DropMe.Services;

namespace DropMe.Desktop.Services;

public class DeviceService : IDeviceService {
    public string GetDeviceString() {
        return "Desktop";
    }
}