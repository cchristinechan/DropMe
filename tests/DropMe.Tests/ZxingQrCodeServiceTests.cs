using NUnit.Framework;
using DropMe.Services;
using System;

namespace DropMe.Tests;

public class ZxingQrCodeServiceTests
{
    [Test]
    public void Generate_ShouldThrow_WhenTextIsEmpty()
    {
        var service = new ZxingQrCodeService();

        Assert.Throws<ArgumentException>(() => service.Generate(""));
        Assert.Throws<ArgumentException>(() => service.Generate("   "));
    }

    // [Test]
    // public void Generate_ShouldReturnBitmap_WhenTextIsValid()
    // {
    //     var service = new ZxingQrCodeService();
    //
    //     var bmp = service.Generate("Hello");
    //
    //     Assert.That(bmp, Is.Not.Null);
    //     Assert.That(bmp.PixelSize.Width, Is.GreaterThan(0));
    //     Assert.That(bmp.PixelSize.Height, Is.GreaterThan(0));
    // }
    //
    // [Test]
    // public void Generate_ShouldProduceLargerBitmap_WhenPixelsPerModuleIsLarger()
    // {
    //     var service = new ZxingQrCodeService();
    //     var text = "DropMe-EndToEnd";
    //
    //     var small = service.Generate(text, 4);
    //     var large = service.Generate(text, 12);
    //
    //     Assert.That(large.PixelSize.Width, Is.GreaterThan(small.PixelSize.Width));
    //     Assert.That(large.PixelSize.Height, Is.GreaterThan(small.PixelSize.Height));
    // }
}