using System;
using System.Text;
using System.Text.Json;

namespace DropMe.Services.Session;

public static class QrCodeDataCodec {
    public static string Encode(QrCodeData invite) {
        return JsonSerializer.Serialize(invite); // Changed to be straight json to allow better data density
    }

    public static bool TryDecode(string text, out QrCodeData? invite) {
        try {
            invite = JsonSerializer.Deserialize<QrCodeData>(text);
            return true;
        }
        catch (JsonException e) {
            Console.WriteLine($"Error deserialising QR code {e.Message}\nRaw json\n{text}");
            invite = null;
            return false;
        }
    }
}