using System;
using NUnit.Framework;
using DropMe.Services.Session;

namespace DropMe.Tests;

public class QrCodeDataTests {
    [Test]
    public void LanConnectionInfo_ShouldStoreProperties() {
        var lan = new LanConnectionInfo("192.168.1.10", 8080);

        Assert.That(lan.Address, Is.EqualTo("192.168.1.10"));
        Assert.That(lan.Port, Is.EqualTo(8080));
    }

    [Test]
    public void BtConnectionInfo_ShouldStoreProperties() {
        var bt = new BtConnectionInfo("AA:BB:CC:DD", "DropMe-Phone");

        Assert.That(bt.Address, Is.EqualTo("AA:BB:CC:DD"));
        Assert.That(bt.Name, Is.EqualTo("DropMe-Phone"));
    }

    [Test]
    public void QrCodeData_ShouldStoreAllProperties() {
        var sid = Guid.NewGuid();
        var lan = new LanConnectionInfo("127.0.0.1", 9000);
        var bt = new BtConnectionInfo("11:22:33:44", "MyDevice");
        var aesKey = new byte[] { 1, 2, 3, 4 };

        var data = new QrCodeData(
            1,
            sid,
            lan,
            bt,
            aesKey
        );

        Assert.That(data.V, Is.EqualTo(1));
        Assert.That(data.Sid, Is.EqualTo(sid));
        Assert.That(data.LanInfo, Is.EqualTo(lan));
        Assert.That(data.BtInfo, Is.EqualTo(bt));
        Assert.That(data.AesKey, Is.EqualTo(aesKey));
    }

    [Test]
    public void QrCodeData_ShouldAllowNullOptionalFields() {
        var sid = Guid.NewGuid();
        var aesKey = new byte[] { 9, 8, 7, 6 };

        var data = new QrCodeData(
            1,
            sid,
            null,
            null,
            aesKey
        );

        Assert.That(data.V, Is.EqualTo(1));
        Assert.That(data.Sid, Is.EqualTo(sid));
        Assert.That(data.LanInfo, Is.Null);
        Assert.That(data.BtInfo, Is.Null);
        Assert.That(data.AesKey, Is.EqualTo(aesKey));
    }

    [Test]
    public void LanConnectionInfo_WithSameValues_ShouldBeEqual() {
        var lan1 = new LanConnectionInfo("192.168.0.5", 1234);
        var lan2 = new LanConnectionInfo("192.168.0.5", 1234);

        Assert.That(lan1, Is.EqualTo(lan2));
    }

    [Test]
    public void BtConnectionInfo_WithSameValues_ShouldBeEqual() {
        var bt1 = new BtConnectionInfo("AA:BB:CC", "DeviceA");
        var bt2 = new BtConnectionInfo("AA:BB:CC", "DeviceA");

        Assert.That(bt1, Is.EqualTo(bt2));
    }
}