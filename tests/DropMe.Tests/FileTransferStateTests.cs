using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using NUnit.Framework;
using DropMe.Services;
using DropMe.Services.Session;

namespace DropMe.Tests;

public class FileTransferStateTests {
    [Test]
    public void AwaitingDecision_ShouldStoreProperties() {
        var offer = new FileOfferInfo(Guid.NewGuid(), "report.pdf", 1024);
        var tcs = new TaskCompletionSource<bool>();

        var state = new AwaitingDecision(offer, tcs);

        Assert.That(state.FileOffer, Is.EqualTo(offer));
        Assert.That(state.DecisionTcs, Is.EqualTo(tcs));
        Assert.That(state, Is.InstanceOf<FileTransferState>());
    }

    [Test]
    public void ReceiveInProgress_ShouldStoreInitialValues() {
        using var stream = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var state = new ReceiveInProgress(
            stream,
            "C:/temp/report.pdf",
            5000,
            1200,
            hash,
            3
        );

        Assert.That(state.SaveStream, Is.EqualTo(stream));
        Assert.That(state.SavePath, Is.EqualTo("C:/temp/report.pdf"));
        Assert.That(state.ExpectedSizeBytes, Is.EqualTo(5000));
        Assert.That(state.WrittenBytes, Is.EqualTo(1200));
        Assert.That(state.Hash, Is.EqualTo(hash));
        Assert.That(state.ExpectedChunkIndex, Is.EqualTo(3));
        Assert.That(state, Is.InstanceOf<FileTransferState>());
    }

    [Test]
    public void ReceiveInProgress_ShouldAllowUpdatingMutableProperties() {
        using var stream = new MemoryStream();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var state = new ReceiveInProgress(
            stream,
            "C:/temp/report.pdf",
            5000,
            1200,
            hash,
            3
        );

        state.WrittenBytes = 2500;
        state.ExpectedChunkIndex = 4;

        Assert.That(state.WrittenBytes, Is.EqualTo(2500));
        Assert.That(state.ExpectedChunkIndex, Is.EqualTo(4));
    }

    [Test]
    public void SendInProgress_ShouldStoreProperties() {
        using var stream = new MemoryStream(new byte[] { 1, 2, 3, 4 });
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var state = new SendInProgress(stream, hash, 7);

        Assert.That(state.Source, Is.EqualTo(stream));
        Assert.That(state.Hash, Is.EqualTo(hash));
        Assert.That(state.NextChunk, Is.EqualTo(7));
        Assert.That(state, Is.InstanceOf<FileTransferState>());
    }

    [Test]
    public void AwaitingAck_ShouldStoreProperties() {
        var hash = new byte[] { 10, 20, 30, 40 };

        var state = new AwaitingAck(
            "C:/temp/report.pdf",
            4096,
            hash
        );

        Assert.That(state.FilePath, Is.EqualTo("C:/temp/report.pdf"));
        Assert.That(state.FileSizeBytes, Is.EqualTo(4096));
        Assert.That(state.Hash.ToArray(), Is.EqualTo(hash));
        Assert.That(state, Is.InstanceOf<FileTransferState>());
    }
}