using DropMe.Services;

namespace DropMe.Android.Services;

public class DeviceService : IDeviceService {
    public string GetDeviceString() {
        return "Android";
    }
}