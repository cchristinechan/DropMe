namespace DropMe.Services;

public interface IPermissionsService {
    public bool HasCameraPermissions { get; }
    public bool HasBluetoothPermissions { get; }

    public void RequestCameraPermission();
    public void RequestBluetoothPermission();
    public void RequestBluetoothDiscoverablePermission(int maxDuration);
}