using System;

namespace DropMe.Services;

public sealed record FileOfferInfo(Guid FileId, string Name, long Size, bool IsDirectory);
