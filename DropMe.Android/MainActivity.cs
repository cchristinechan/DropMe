using Android.App;
using Android.Content.PM;
using Android.OS;
using Android;
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

namespace DropMe.Android;

[Activity(
    Label = "DropMe.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App> {
    public static AndroidX.Activity.ComponentActivity? CurrentActivity { get; private set; }
    private const int CameraRequestCode = 1001;

    protected override void OnCreate(Bundle? savedInstanceState) {
        base.OnCreate(savedInstanceState);
        CurrentActivity = this;

        EnsureCameraPermission();
    }

    private void EnsureCameraPermission() {
        if (ContextCompat.CheckSelfPermission(this, Manifest.Permission.Camera) == Permission.Granted)
            return;

        ActivityCompat.RequestPermissions(
            this,
            new[] { Manifest.Permission.Camera },
            CameraRequestCode);
    }

    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) {
        var services = new ServiceCollection();
        services.AddCrossPlatformServices();

        // Add platform specific services here
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<ICameraService, AndroidCameraService>();
        services.AddSingleton<IStorageService, AndroidStorageService>();

        App.Services = services.BuildServiceProvider();

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}