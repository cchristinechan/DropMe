using Avalonia;
using DropMe.Services;
using DropMe.Services.Session;
using DropMe.ViewModels;
using DropMe.Views;
using Microsoft.Extensions.DependencyInjection;

namespace DropMe.Tests;

public class Tests {
    [SetUp]
    public void Setup() {
    }

    [Test]
    public void CrossPlatformServicesAvailable() {
        // Configure services
        var services = new ServiceCollection();
        services.AddCrossPlatformServices();
        App.Services = services.BuildServiceProvider();

        // Check app contains services
        Assert.That(App.Services, Is.Not.Null);

        // Check necessary services exist
        Assert.DoesNotThrow(() => App.Services.GetRequiredService<IWorkManager>());
        Assert.DoesNotThrow(() => App.Services.GetRequiredService<IQrCodeService>());
        Assert.DoesNotThrow(() => App.Services.GetRequiredService<IPermissionsService>());
    }
}
