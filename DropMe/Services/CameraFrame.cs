namespace DropMe.Services;

public sealed record CameraFrame(
    int Width,
    int Height,
    byte[] Rgba,   // RGBA8888 pixels
    int Stride,     // bytes per row
    int Rotation
);