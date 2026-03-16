using System.Threading.Tasks;

namespace DropMe.Services;

public interface IPermissionsService {
    public bool HasCameraPermissions { get; }
    public bool HasBluetoothPermissions { get; }

    public Task RequestCameraPermission();
    public Task RequestBluetoothPermission();
    public Task RequestBluetoothDiscoverablePermission(int maxDuration);
}