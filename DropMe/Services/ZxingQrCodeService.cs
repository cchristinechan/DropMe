using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ZXing.QrCode;
using ZXing.QrCode.Internal;

namespace DropMe.Services;

public sealed class ZxingQrCodeService : IQrCodeService {
    public Bitmap Generate(string text, int pixelsPerModule = 8) {
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("QR text cannot be empty.", nameof(text));

        var qr = Encoder.encode(text, ErrorCorrectionLevel.M);
        var matrix = qr.Matrix;

        int modules = matrix.Width;
        int scale = Math.Max(1, pixelsPerModule);
        int margin = 2;

        int width = (modules + margin * 2) * scale;
        int height = width;

        var useRgba = OperatingSystem.IsAndroid();
        var pixelFormat = useRgba ? PixelFormat.Rgba8888 : PixelFormat.Bgra8888;

        var bmp = new WriteableBitmap(
            new PixelSize(width, height),
            new Vector(96, 96),
            pixelFormat,
            AlphaFormat.Unpremul);

        using var fb = bmp.Lock();
        int rowBytes = fb.RowBytes;
        var row = new byte[rowBytes];

        for (int y = 0; y < height; y++) {
            // white row
            for (int i = 0; i < row.Length; i += 4) {
                if (useRgba) {
                    row[i + 0] = 0xFF; // R
                    row[i + 1] = 0xFF; // G
                    row[i + 2] = 0xFF; // B
                    row[i + 3] = 0xFF; // A
                }
                else {
                    row[i + 0] = 0xFF; // B
                    row[i + 1] = 0xFF; // G
                    row[i + 2] = 0xFF; // R
                    row[i + 3] = 0xFF; // A
                }
            }

            int my = (y / scale) - margin;

            if ((uint)my < (uint)modules) {
                for (int x = 0; x < width; x++) {
                    int mx = (x / scale) - margin;
                    if ((uint)mx >= (uint)modules) continue;

                    if (matrix[mx, my] == 1) {
                        int px = x * 4;
                        if (useRgba) {
                            row[px + 0] = 0x00;
                            row[px + 1] = 0x00;
                            row[px + 2] = 0x00;
                            row[px + 3] = 0xFF;
                        }
                        else {
                            row[px + 0] = 0x00;
                            row[px + 1] = 0x00;
                            row[px + 2] = 0x00;
                            row[px + 3] = 0xFF;
                        }
                    }
                }
            }

            Marshal.Copy(row, 0, fb.Address + (y * rowBytes), row.Length);
        }

        return bmp;
    }
}
