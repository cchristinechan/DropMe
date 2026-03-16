using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content.PM;
using Android.OS;
using Android;
using Android.Bluetooth;
using Android.Content;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using Avalonia;
using Avalonia.Android;
using DropMe.Android;
using DropMe.Android.Services;
using DropMe.Services;
using Microsoft.Extensions.DependencyInjection;
using AndroidX.Activity;
using AndroidX.Activity.Result.Contract;
using InTheHand.Net.Sockets;

namespace DropMe.Android;

[Activity(
    Label = "DropMe",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App> {
    public const int BLUETOOTH_DISCOVERABLE_RQ = 0;
    public static AndroidX.Activity.ComponentActivity? CurrentActivity { get; private set; }
    protected override void OnActivityResult(int requestCode, Result resultCode, Intent? data) {
        base.OnActivityResult(requestCode, resultCode, data);
        try {
            switch (requestCode) {
                case BLUETOOTH_DISCOVERABLE_RQ:
                    ((AndroidPermissionsService)App.Services.GetService<IPermissionsService>()).BluetoothDiscoverableResponse
                        .TrySetResult(resultCode == Result.Ok);
                    break;
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"Exception {ex.Message}");
        }
    }

    protected override void OnCreate(Bundle? savedInstanceState) {
        base.OnCreate(savedInstanceState);
        CurrentActivity = this;
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) {
        InTheHand.AndroidActivity.CurrentActivity = this;
        var services = new ServiceCollection();
        services.AddCrossPlatformServices();

        // Add platform specific services here
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<ICameraService, AndroidCameraService>();
        services.AddSingleton<IStorageService, AndroidStorageService>();
        services.AddSingleton<IPermissionsService>(sp => ActivatorUtilities.CreateInstance<AndroidPermissionsService>(sp, this));

        App.Services = services.BuildServiceProvider();

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}
