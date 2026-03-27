using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using DropMe.Services;

namespace DropMe.Tests;

public class StreamIoExtensionsTests
{
    [Test]
    public async Task ReadExactlyAsync_ShouldFillBuffer_WhenEnoughBytesAvailable()
    {
        var source = new byte[] { 10, 20, 30, 40, 50 };
        using var stream = new MemoryStream(source);
        var buffer = new byte[3];

        await stream.ReadExactlyAsync(buffer, 0, 3);

        Assert.That(buffer, Is.EqualTo(new byte[] { 10, 20, 30 }));
    }

    [Test]
    public void ReadExactlyAsync_ShouldThrow_WhenStreamEndsEarly()
    {
        var source = new byte[] { 1, 2 };
        using var stream = new MemoryStream(source);
        var buffer = new byte[4];

        Assert.That(async () => await stream.ReadExactlyAsync(buffer, 0, 4),
            Throws.TypeOf<EndOfStreamException>());
    }

    [Test]
    public async Task WriteAndReadUInt32LEAsync_ShouldPreserveValue()
    {
        const uint original = 0x12345678;
        using var stream = new MemoryStream();

        await stream.WriteUInt32LEAsync(original);
        stream.Position = 0;

        var value = await stream.ReadUInt32LEAsync();

        Assert.That(value, Is.EqualTo(original));
    }

    [Test]
    public async Task WriteAndReadUInt16LEAsync_ShouldPreserveValue()
    {
        const ushort original = 0x1234;
        using var stream = new MemoryStream();

        await stream.WriteUInt16LEAsync(original);
        stream.Position = 0;

        var value = await stream.ReadUInt16LEAsync();

        Assert.That(value, Is.EqualTo(original));
    }

    [Test]
    public async Task WriteAndReadInt64LEAsync_ShouldPreserveValue()
    {
        const long original = 0x0102030405060708L;
        using var stream = new MemoryStream();

        await stream.WriteInt64LEAsync(original);
        stream.Position = 0;

        var value = await stream.ReadInt64LEAsync();

        Assert.That(value, Is.EqualTo(original));
    }

    [Test]
    public void WriteUInt32LE_AndReadUInt32LE_ShouldPreserveValue()
    {
        const uint original = 0x89ABCDEF;
        var buffer = new byte[4];

        buffer.AsSpan().WriteUInt32LE(original);
        var value = buffer.AsSpan().ReadUInt32LE();

        Assert.That(value, Is.EqualTo(original));
    }

    [Test]
    public void Utf8_ShouldReturnUtf8Bytes()
    {
        var text = "Hello世界";
        var bytes = StreamIoExtensions.Utf8(text);

        Assert.That(bytes, Is.EqualTo(Encoding.UTF8.GetBytes(text)));
    }
}