using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DropMe.Services.Session;

namespace DropMe.Services;

public static class AesGcmFileTransfer {

    public static string SanitizeName(string name) {
        return Path.GetInvalidFileNameChars().Aggregate(name, (current, c) => current.Replace(c, '_'));
    }

}