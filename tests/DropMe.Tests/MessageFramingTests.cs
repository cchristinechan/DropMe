using System;
using System.IO;
using NUnit.Framework;
using DropMe.Services.Session;

namespace DropMe.Tests;

public class MessageFramingTests
{
    private static byte[] Combine(byte[] header, byte[] body)
    {
        var data = new byte[header.Length + body.Length];
        Array.Copy(header, 0, data, 0, header.Length);
        Array.Copy(body, 0, data, header.Length, body.Length);
        return data;
    }

    [Test]
    public void FrameAndParse_PingMessage_ShouldWork()
    {
        var msg = new PingMsg();
        uint acknowledges = 123u;

        var (header, body) = MessageFraming.FrameMessage(msg, acknowledges);
        var data = Combine(header, body);

        var (parsedMsg, parsedAcknowledges) = MessageFraming.ParseMessage(data);

        Assert.That(parsedMsg, Is.TypeOf<PingMsg>());
        Assert.That(parsedAcknowledges, Is.EqualTo(acknowledges));
    }

    [Test]
    public void ParseMessage_ShouldThrow_WhenMagicIsInvalid()
    {
        var msg = new PingMsg();
        var (header, body) = MessageFraming.FrameMessage(msg, 1u);
        var data = Combine(header, body);

        data[0] = (byte)'X'; // 破坏 magic

        Assert.That(() => MessageFraming.ParseMessage(data),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void ParseMessage_ShouldThrow_WhenVersionIsInvalid()
    {
        var msg = new PingMsg();
        var (header, body) = MessageFraming.FrameMessage(msg, 1u);
        var data = Combine(header, body);

        data[4] = 99; // 破坏 version

        Assert.That(() => MessageFraming.ParseMessage(data),
            Throws.TypeOf<InvalidDataException>());
    }

    [Test]
    public void FrameAndParse_ShouldPreserveAcknowledges()
    {
        var msg = new PingMsg();
        uint acknowledges = 0x12345678;

        var (header, body) = MessageFraming.FrameMessage(msg, acknowledges);
        var data = Combine(header, body);

        var (_, parsedAcknowledges) = MessageFraming.ParseMessage(data);

        Assert.That(parsedAcknowledges, Is.EqualTo(acknowledges));
    }
}