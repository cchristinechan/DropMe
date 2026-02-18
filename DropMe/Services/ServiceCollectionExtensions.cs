using DropMe.ViewModels;
using DropMe.Views;
using Microsoft.Extensions.DependencyInjection;
using DropMe.Services.Session;
namespace DropMe.Services;

public static class ServiceCollectionExtensions {
    public static void AddCrossPlatformServices(this IServiceCollection services) {
        // Work scheduling (cross-platform)
        services.AddSingleton<IWorkManager, WorkManager>();
        // File Transfer
        services.AddSingleton<IFileTransfer, TcpAeadFileTransfer>();
        // QR scanning / generation (cross-platform)
        services.AddSingleton<QrDecoder>();
        services.AddSingleton<IQrCodeService, ZxingQrCodeService>();
        //Session Factory
        services.AddSingleton<SessionFactory>();

        // Register cross platform services here
        services.AddTransient<MainViewModel>();

    }
}