using Avalonia.Media.Imaging;

namespace DropMe.Services;

public interface IQrCodeService {
    Bitmap Generate(string text, int pixelsPerModule = 8);
}