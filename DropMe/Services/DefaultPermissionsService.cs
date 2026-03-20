using System.Threading.Tasks;

namespace DropMe.Services;

public class DefaultPermissionsService : IPermissionsService {
    public bool HasCameraPermissions => true;
    public bool HasBluetoothPermissions => true;
    public bool HasBluetoothDiscoverablePermissions => true;
    public Task RequestCameraPermission() {
        return Task.CompletedTask;
    }

    public Task RequestBluetoothPermission() {
        return Task.CompletedTask;
    }

    public Task RequestBluetoothDiscoverablePermission(int maxDuration) {
        return Task.CompletedTask;
    }
}