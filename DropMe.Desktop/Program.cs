using System;
using Avalonia;
using DropMe.Services;
using DropMe.Desktop.Services;
using Microsoft.Extensions.DependencyInjection;

namespace DropMe.Desktop;

sealed class Program {
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp()
        .StartWithClassicDesktopLifetime(args);

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp() {
        var services = new ServiceCollection();
        services.AddCrossPlatformServices();

        // Add desktop specific services here
        services.AddSingleton<IDeviceService, DeviceService>();
        services.AddSingleton<ICameraService, DesktopCameraService>();
        services.AddSingleton<IStorageService, DesktopStorageService>();

        App.Services = services.BuildServiceProvider();

        return AppBuilder.Configure<App>()
            .UseSkia()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
    }
}