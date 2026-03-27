using NUnit.Framework;
using DropMe.Services;
using System;

namespace DropMe.Tests;

public class QrDecoderTests {
    private CameraFrame CreateEmptyFrame() {
        return new CameraFrame(
            Width: 10,
            Height: 10,
            Rgba: Array.Empty<byte>(),
            Stride: 10 * 4,
            Rotation: 0
        );
    }

    private CameraFrame CreateNullFrame() {
        return new CameraFrame(
            Width: 10,
            Height: 10,
            Rgba: null!,
            Stride: 10 * 4,
            Rotation: 0
        );
    }

    private CameraFrame CreateSolidColorFrame(byte r, byte g, byte b, int width = 10, int height = 10) {
        var data = new byte[width * height * 4];

        for (int i = 0; i < data.Length; i += 4) {
            data[i + 0] = b;
            data[i + 1] = g;
            data[i + 2] = r;
            data[i + 3] = 255;
        }

        return new CameraFrame(
            width,
            height,
            data,
            width * 4,
            0
        );
    }

    [Test]
    public void TryDecode_ShouldReturnNull_WhenRgbaIsNull() {
        var decoder = new QrDecoder();
        var frame = CreateNullFrame();

        var result = decoder.TryDecode(frame);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryDecode_ShouldReturnNull_WhenRgbaIsEmpty() {
        var decoder = new QrDecoder();
        var frame = CreateEmptyFrame();

        var result = decoder.TryDecode(frame);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryDecode_ShouldReturnNull_WhenFrameHasNoQrCode() {
        var decoder = new QrDecoder();

        // pure blank image
        var frame = CreateSolidColorFrame(255, 255, 255);

        var result = decoder.TryDecode(frame);

        Assert.That(result, Is.Null);
    }

    [Test]
    public void TryDecode_ShouldReturnDecodedText_WhenValidQrCode() {
        var decoder = new QrDecoder();
        var expectedText = "DropMe-Test";

        var writer = new ZXing.BarcodeWriterPixelData {
            Format = ZXing.BarcodeFormat.QR_CODE,
            Options = new ZXing.Common.EncodingOptions {
                Width = 100,
                Height = 100
            }
        };

        var pixelData = writer.Write(expectedText);


        var frame = new CameraFrame(
            pixelData.Width,
            pixelData.Height,
            pixelData.Pixels,
            pixelData.Width * 4,
            0
        );

        var result = decoder.TryDecode(frame);

        Assert.That(result, Is.EqualTo(expectedText));
    }
}