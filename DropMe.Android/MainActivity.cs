using Android.App;
using Android.Content.PM;
using Avalonia;
using Avalonia.Android;
using DropMe.Android;
using DropMe.Android.Services;
using DropMe.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DropMe.Android;

[Activity(
    Label = "DropMe.Android",
    Theme = "@style/MyTheme.NoActionBar",
    Icon = "@drawable/icon",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity<App> {
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder) {
        var services = new ServiceCollection();
        services.AddCrossPlatformServices();

        // Add platform specific services here
        services.AddSingleton<IDeviceService, DeviceService>();

        App.Services = services.BuildServiceProvider();

        return base.CustomizeAppBuilder(builder)
            .WithInterFont();
    }
}