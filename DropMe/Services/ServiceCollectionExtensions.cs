using DropMe.ViewModels;
using DropMe.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DropMe.Services;

public static class ServiceCollectionExtensions {
    public static void AddCrossPlatformServices(this IServiceCollection services) {
        // Work scheduling (cross-platform)
        services.AddSingleton<IWorkManager, WorkManager>();
        // File Transfer
        services.AddSingleton<IFileTransfer, TcpAeadFileTransfer>();
        // Register cross platform services here
        services.AddTransient<MainViewModel>();
    }
}