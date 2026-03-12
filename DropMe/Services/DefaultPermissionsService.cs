namespace DropMe.Services;

public class DefaultPermissionsService : IPermissionsService {
    public bool HasCameraPermissions => true;
    public bool HasBluetoothPermissions => true;
    public void RequestCameraPermission() {
        
    }

    public void RequestBluetoothPermission() {
        
    }

    public void RequestBluetoothDiscoverablePermission(int maxDuration) {
         
    }
}