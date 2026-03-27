using System;
using NUnit.Framework;
using DropMe.Services.Session;

namespace DropMe.Tests;

public class QrCodeDataCodecTests
{
    private QrCodeData CreateSampleData()
    {
        return new QrCodeData(
            1,
            Guid.NewGuid(),
            new LanConnectionInfo("127.0.0.1", 8080),
            null,
            new byte[] { 1, 2, 3 }
        );
    }

    [Test]
    public void EncodeThenDecode_ShouldPreserveData()
    {
        var original = CreateSampleData();

        var json = QrCodeDataCodec.Encode(original);
        var success = QrCodeDataCodec.TryDecode(json, out var decoded);

        Assert.That(success, Is.True);
        Assert.That(decoded, Is.Not.Null);

        Assert.That(decoded!.V, Is.EqualTo(original.V));
        Assert.That(decoded.Sid, Is.EqualTo(original.Sid));
        Assert.That(decoded.LanInfo, Is.Not.Null);
        Assert.That(decoded.LanInfo!.Address, Is.EqualTo(original.LanInfo!.Address));
        Assert.That(decoded.LanInfo.Port, Is.EqualTo(original.LanInfo.Port));
        Assert.That(decoded.BtInfo, Is.Null);
        Assert.That(decoded.AesKey, Is.EqualTo(original.AesKey));
    }
    
    [Test]
    public void EncodeThenDecode_ShouldWork()
    {
        var original = CreateSampleData();

        var json = QrCodeDataCodec.Encode(original);
        var success = QrCodeDataCodec.TryDecode(json, out var decoded);

        Assert.That(success, Is.True);
        Assert.That(decoded, Is.Not.Null);
    }

    [Test]
    public void TryDecode_ShouldFail_OnInvalidJson()
    {
        var success = QrCodeDataCodec.TryDecode("invalid", out var decoded);

        Assert.That(success, Is.False);
        Assert.That(decoded, Is.Null);
    }
}