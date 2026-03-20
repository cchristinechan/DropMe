using System;
using System.Threading.Tasks;
using Android;
using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using DropMe.Services;
using InTheHand.Net.Bluetooth;

namespace DropMe.Android.Services;
// Correct this for different api levels
public class AndroidPermissionsService(Activity activity) : IPermissionsService {
    private Activity _activity = activity;
    public TaskCompletionSource<bool> BluetoothDiscoverableResponse { get; private set; } = new();
    public bool HasCameraPermissions => ContextCompat.CheckSelfPermission(_activity, Manifest.Permission.Camera) == Permission.Granted;

    public bool HasBluetoothPermissions {
        get {
            if (OperatingSystem.IsAndroidVersionAtLeast(31)) {
                return ContextCompat.CheckSelfPermission(_activity, Manifest.Permission.BluetoothScan) == Permission.Granted &&
                    ContextCompat.CheckSelfPermission(_activity, Manifest.Permission.BluetoothConnect) ==
                    Permission.Granted &&
                    ContextCompat.CheckSelfPermission(_activity, Manifest.Permission.BluetoothAdvertise) ==
                    Permission.Granted;
            }
            return false;
        }
    }

    public bool HasBluetoothDiscoverablePermissions => BluetoothRadio.Default.Mode == RadioMode.Discoverable;

    public Task RequestCameraPermission() {
        _activity.RequestPermissions([Manifest.Permission.Camera], 0);
        return Task.CompletedTask;
    }

    public Task RequestBluetoothPermission() {
        if (OperatingSystem.IsAndroidVersionAtLeast(31)) {
            _activity.RequestPermissions([
                Manifest.Permission.BluetoothScan,
                Manifest.Permission.BluetoothConnect,
                Manifest.Permission.BluetoothAdvertise,
                Manifest.Permission.Camera
            ], 0);
        }
        return Task.CompletedTask;
    }

    public Task RequestBluetoothDiscoverablePermission(int maxDuration) {
        BluetoothDiscoverableResponse = new();
        var intent = new Intent(BluetoothAdapter.ActionRequestDiscoverable);
        intent.PutExtra(BluetoothAdapter.ExtraDiscoverableDuration, maxDuration);
        _activity.StartActivityForResult(intent, MainActivity.BLUETOOTH_DISCOVERABLE_RQ);
        return BluetoothDiscoverableResponse.Task;
    }
}