using System;
using System.Net;
using InTheHand.Net;

namespace DropMe.Services.Session;

public sealed record LanConnectionInfo(string Address, int Port);

public sealed record BtConnectionInfo(string? Address, string Name);

public sealed record QrCodeData(
    int V,
    Guid Sid,
    LanConnectionInfo? LanInfo,
    BtConnectionInfo? BtInfo,
    byte[] AesKey
);