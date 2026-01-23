using DropMe.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace DropMe.Services;

public static class ServiceCollectionExtensions {
    public static void AddCrossPlatformServices(this IServiceCollection collection) {
        collection.AddTransient<MainViewModel>();
    }
}