using ZXing;
using ZXing.Common;

namespace DropMe.Services;

public sealed class QrDecoder {
    private readonly BarcodeReaderGeneric _reader = new() {
        AutoRotate = true,
        Options = new DecodingOptions {
            TryHarder = true,
            PossibleFormats = new[] { BarcodeFormat.QR_CODE }
        }
    };

    public string? TryDecode(CameraFrame frame) {
        if (frame.Rgba is null || frame.Rgba.Length == 0) return null;

        // Convert BGRA bytes -> grayscale (Y) buffer, respecting stride.
        // (If your camera is still RGBA, this still works fine—just slightly different channel weighting.)
        var y = new byte[frame.Width * frame.Height];

        int dst = 0;
        int srcRowStart = 0;

        for (int row = 0; row < frame.Height; row++) {
            int src = srcRowStart;
            for (int col = 0; col < frame.Width; col++) {
                // frame bytes are BGRA (B,G,R,A)
                byte b = frame.Rgba[src + 0];
                byte g = frame.Rgba[src + 1];
                byte r = frame.Rgba[src + 2];

                // Luma approximation
                y[dst++] = (byte)((r * 77 + g * 150 + b * 29) >> 8);

                src += 4;
            }
            srcRowStart += frame.Stride;
        }

        // Build a luminance source from the Y plane (no stride issues now)
        var source = new PlanarYUVLuminanceSource(
            y,
            frame.Width,
            frame.Height,
            0, 0,
            frame.Width,
            frame.Height,
            false);

        var result = _reader.Decode(source);
        return string.IsNullOrWhiteSpace(result?.Text) ? null : result.Text.Trim();
    }
}