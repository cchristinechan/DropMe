using System.Threading.Tasks;
using NUnit.Framework;
using DropMe.Services;

namespace DropMe.Tests;

public class DefaultPermissionsServiceTests
{
    [Test]
    public void HasCameraPermissions_ShouldBeTrue()
    {
        var service = new DefaultPermissionsService();

        Assert.That(service.HasCameraPermissions, Is.True);
    }

    [Test]
    public void HasBluetoothPermissions_ShouldBeTrue()
    {
        var service = new DefaultPermissionsService();

        Assert.That(service.HasBluetoothPermissions, Is.True);
    }

    [Test]
    public void HasBluetoothDiscoverablePermissions_ShouldBeTrue()
    {
        var service = new DefaultPermissionsService();

        Assert.That(service.HasBluetoothDiscoverablePermissions, Is.True);
    }

    [Test]
    public async Task RequestCameraPermission_ShouldCompleteSuccessfully()
    {
        var service = new DefaultPermissionsService();

        Assert.DoesNotThrowAsync(async () => await service.RequestCameraPermission());
    }

    [Test]
    public async Task RequestBluetoothPermission_ShouldCompleteSuccessfully()
    {
        var service = new DefaultPermissionsService();

        Assert.DoesNotThrowAsync(async () => await service.RequestBluetoothPermission());
    }

    [Test]
    public async Task RequestBluetoothDiscoverablePermission_ShouldCompleteSuccessfully()
    {
        var service = new DefaultPermissionsService();

        Assert.DoesNotThrowAsync(async () => await service.RequestBluetoothDiscoverablePermission(60));
    }
}