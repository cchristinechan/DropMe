using System;
using System.Text;
using System.Text.Json;

namespace DropMe.Services.Session;

public static class ConnectionInviteCodec {
    private const string PrefixV1 = "DM1:";
    private const string PrefixV2 = "DM2:";

    public static string Encode(ConnectionInvite invite) {
        var json = JsonSerializer.Serialize(invite);
        var bytes = Encoding.UTF8.GetBytes(json);
        var prefix = invite.V >= 2 ? PrefixV2 : PrefixV1;
        return prefix + Base64UrlEncode(bytes);
    }

    public static bool TryDecode(string text, out ConnectionInvite? invite) {
        invite = null;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        string prefix;
        if (text.StartsWith(PrefixV2, StringComparison.Ordinal))
            prefix = PrefixV2;
        else if (text.StartsWith(PrefixV1, StringComparison.Ordinal))
            prefix = PrefixV1;
        else
            return false;

        try {
            var b64 = text.Substring(prefix.Length);
            var bytes = Base64UrlDecode(b64);
            invite = JsonSerializer.Deserialize<ConnectionInvite>(Encoding.UTF8.GetString(bytes));
            return invite is not null;
        }
        catch { return false; }
    }

    public static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public static byte[] Base64UrlDecode(string s) {
        s = s.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4) {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}