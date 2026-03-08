using DropMe.ViewModels;
using DropMe.Views;
using Microsoft.Extensions.DependencyInjection;
using DropMe.Services.Session;
namespace DropMe.Services;

public static class ServiceCollectionExtensions {
    /// <summary>
    /// Adds all services that are the same across all platforms to a service collection.
    /// </summary>
    /// <param name="services">The services collection to be initialised.</param>
    public static void AddCrossPlatformServices(this IServiceCollection services) {
        // Work scheduling (cross-platform)
        services.AddSingleton<IWorkManager, WorkManager>();
        // QR scanning / generation (cross-platform)
        services.AddSingleton<QrDecoder>();
        services.AddSingleton<IQrCodeService, ZxingQrCodeService>();
        //Session Factory
        services.AddSingleton<ConfigService>();
        // Register cross platform services here
        services.AddTransient<MainViewModel>();

    }
}