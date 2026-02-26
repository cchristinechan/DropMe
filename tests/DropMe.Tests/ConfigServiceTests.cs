using Avalonia;
using DropMe.Services;
using DropMe.Services.Session;
using Microsoft.Extensions.DependencyInjection;

namespace DropMe.Tests;

public class ConfigServiceTests {
    ServiceProvider _services;

    [SetUp]
    public void Setup() {
        var collection = new ServiceCollection();
        collection.AddSingleton<IStorageService, ConfigServiceTestsStorageMock>();
        collection.AddSingleton<ConfigService>();
        _services = collection.BuildServiceProvider();
    }

    [TearDown]
    public void TearDown() {
        _services.Dispose();
    }

    [Test]
    public void InsertKVEmptyInitialStream() {
        var storage = (ConfigServiceTestsStorageMock)_services.GetService<IStorageService>()!;
        var backing = new byte[1000];
        storage.ReadStream = new MemoryStream();
        storage.WriteStream = new MemoryStream(backing, true);
        var config = _services.GetService<ConfigService>()!;

        config.InsertKVPair("Key", "Value");
        storage.ReadStream = new MemoryStream(backing, false);
        Assert.That(config.GetValue("Key"), Is.EqualTo("Value"));
    }

    [Test]
    public void InsertMultipleKV() {
        // Shuffling about of streams due to config by design disposing
        // the streams it uses after it's done with them. Makes it hard to 
        // test without manually moving the streams about to be what config will
        // expect to see

        var storage = (ConfigServiceTestsStorageMock)_services.GetService<IStorageService>()!;
        var backing = new byte[1000];
        storage.ReadStream = new MemoryStream();
        storage.WriteStream = new MemoryStream(backing, true); ;
        var config = _services.GetService<ConfigService>()!;

        config.InsertKVPair("Key", "Value");
        storage.ReadStream = new MemoryStream(backing, false);
        Assert.That(config.GetValue("Key"), Is.EqualTo("Value"));

        storage.ReadStream = new MemoryStream(backing, false);
        storage.WriteStream = new MemoryStream();
        config.InsertKVPair("Key2", "Value2");

        Assert.Multiple(() => {
            Assert.That(config.GetValue("Key"), Is.EqualTo("Value"));
            Assert.That(config.GetValue("Key2"), Is.EqualTo("Value2"));
        });
    }

    // If the internal storage changes ever this test will need to change
    [Test]
    public void PreExistingKVStore() {
        var storage = (ConfigServiceTestsStorageMock)_services.GetService<IStorageService>()!;
        var readStream = new MemoryStream();
        using var writer = new StreamWriter(readStream, leaveOpen: true);
        var map = new Dictionary<string, string> {
            ["key"] = "value"
        };
        writer.Write(System.Text.Json.JsonSerializer.Serialize(map));
        writer.Flush();
        readStream.Position = 0;
        storage.ReadStream = readStream;
        storage.WriteStream = new MemoryStream();
        var config = _services.GetService<ConfigService>()!;

        Assert.That(config.GetValue("key"), Is.EqualTo("value"));
    }
}

public class ConfigServiceTestsStorageMock : IStorageService {
    public Task PickDownloadsFolderAsync(Visual? visual) {
        throw new NotImplementedException();
    }

    public (Stream, string)? OpenDownloadFileWriteStreamAsync(string fileName) {
        throw new NotImplementedException();
    }

    public string? GetDownloadDirectoryLabel() {
        throw new NotImplementedException();
    }

    public Stream ReadConfig() => ReadStream;

    public Stream WriteConfig() => WriteStream;

    // Allow setting of whatever streams are convenient for the users of the service to see
    // for testing
    public Stream ReadStream;
    public Stream WriteStream;
}