using DropMe.ViewModels;
using DropMe.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DropMe.Services;

public static class ServiceCollectionExtensions {
    public static void AddCrossPlatformServices(this IServiceCollection services) {
        // Register cross platform services here
        services.AddTransient<MainView>();
        services.AddTransient<MainViewModel>();
    }
}