namespace DropMe.Services.Session;

public sealed record ConnectionInvite(
    int V,
    string Ip,
    int Port,
    string Sid,
    string Psk, // Base64Url 32 bytes
    string? Transport,
    string? Ssid,
    string? Bssid
);