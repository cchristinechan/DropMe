using System;
using NUnit.Framework;
using DropMe.Services.Session;

namespace DropMe.Tests;

public class SessionMessageTests {
    [Test]
    public void DeviceNameMsg_ShouldStoreName() {
        var msg = new DeviceNameMsg("Alice");

        Assert.That(msg.Name, Is.EqualTo("Alice"));
    }

    [Test]
    public void FileOfferMsg_ShouldStoreProperties() {
        var fileId = Guid.NewGuid();
        var msg = new FileOfferMsg(fileId, "report.pdf", 1024);

        Assert.That(msg.FileId, Is.EqualTo(fileId));
        Assert.That(msg.Name, Is.EqualTo("report.pdf"));
        Assert.That(msg.Size, Is.EqualTo(1024));
    }

    [Test]
    public void FileAcceptMsg_ShouldStoreFileId() {
        var fileId = Guid.NewGuid();
        var msg = new FileAcceptMsg(fileId);

        Assert.That(msg.FileId, Is.EqualTo(fileId));
        Assert.That(msg, Is.InstanceOf<FileMsg>());
    }

    [Test]
    public void FileRejectMsg_ShouldStoreReason() {
        var fileId = Guid.NewGuid();
        var msg = new FileRejectMsg(fileId, FileRejectReason.HashMismatch);

        Assert.That(msg.FileId, Is.EqualTo(fileId));
        Assert.That(msg.Reason, Is.EqualTo(FileRejectReason.HashMismatch));
    }

    [Test]
    public void PingMsg_ShouldBeControlMessage() {
        var msg = new PingMsg();

        Assert.That(msg, Is.InstanceOf<ControlMsg>());
        Assert.That(msg, Is.InstanceOf<DropMeMsg>());
    }

    [Test]
    public void FileOfferMsg_ShouldBeFileMessage() {
        var msg = new FileOfferMsg(Guid.NewGuid(), "file.txt", 10);

        Assert.That(msg, Is.InstanceOf<FileMsg>());
        Assert.That(msg, Is.InstanceOf<DropMeMsg>());
    }

    [Test]
    public void DeviceNameMsg_WithSameName_ShouldBeEqual() {
        var msg1 = new DeviceNameMsg("Alice");
        var msg2 = new DeviceNameMsg("Alice");

        Assert.That(msg1, Is.EqualTo(msg2));
    }

    [Test]
    public void FileChunkMsg_ShouldStoreChunkData() {
        var fileId = Guid.NewGuid();
        var bytes = new byte[] { 1, 2, 3, 4 };
        var msg = new FileChunkMsg(fileId, 7, bytes);

        Assert.That(msg.FileId, Is.EqualTo(fileId));
        Assert.That(msg.ChunkIndex, Is.EqualTo(7));
        Assert.That(msg.Data.ToArray(), Is.EqualTo(bytes));
    }

    [Test]
    public void FileDoneMsg_ShouldStoreSha256() {
        var fileId = Guid.NewGuid();
        var hash = new byte[] { 10, 20, 30, 40 };

        var msg = new FileDoneMsg(fileId, hash);

        Assert.That(msg.FileId, Is.EqualTo(fileId));
        Assert.That(msg.Sha256.ToArray(), Is.EqualTo(hash));
    }

    [Test]
    public void FileAckMsg_ShouldStoreSha256() {
        var fileId = Guid.NewGuid();
        var hash = new byte[] { 5, 6, 7, 8 };

        var msg = new FileAckMsg(fileId, hash);

        Assert.That(msg.FileId, Is.EqualTo(fileId));
        Assert.That(msg.Sha256.ToArray(), Is.EqualTo(hash));
    }

    [Test]
    public void DisconnectMsg_ShouldBeControlMessage() {
        var msg = new DisconnectMsg();

        Assert.That(msg, Is.InstanceOf<ControlMsg>());
        Assert.That(msg, Is.InstanceOf<DropMeMsg>());
    }
}