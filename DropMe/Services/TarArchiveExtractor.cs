using System;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DropMe.Services;

public static class TarArchiveExtractor {
    public static async Task ExtractToDirectoryAsync(string archivePath, string destinationDirectory, CancellationToken cancellationToken) {
        await using var archiveStream = File.OpenRead(archivePath);
        await ExtractToDirectoryAsync(archiveStream, destinationDirectory, cancellationToken);
    }

    public static async Task ExtractToDirectoryAsync(Stream archiveStream, string destinationDirectory, CancellationToken cancellationToken) {
        Directory.CreateDirectory(destinationDirectory);
        var rootPath = Path.GetFullPath(destinationDirectory);

        using var tarReader = new TarReader(archiveStream, leaveOpen: true);
        while (tarReader.GetNextEntry(copyData: false) is { } entry) {
            cancellationToken.ThrowIfCancellationRequested();

            var entryPath = NormalizeTarEntryPath(entry.Name);
            if (string.IsNullOrWhiteSpace(entryPath))
                continue;

            var destinationPath = Path.GetFullPath(Path.Combine(rootPath, entryPath));
            if (!destinationPath.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Tar entry attempts to escape the target directory.");

            if (entry.EntryType is TarEntryType.Directory) {
                Directory.CreateDirectory(destinationPath);
                continue;
            }

            if (!IsRegularFile(entry.EntryType) || entry.DataStream is null)
                continue;

            var parentDirectory = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrWhiteSpace(parentDirectory))
                Directory.CreateDirectory(parentDirectory);

            await using var output = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await entry.DataStream.CopyToAsync(output, cancellationToken);
        }
    }

    public static string NormalizeTarEntryPath(string entryName) {
        var normalized = entryName.Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var segments = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        foreach (var segment in segments) {
            if (segment is "." or "..")
                throw new InvalidDataException("Tar entry contains an invalid relative path.");
        }

        return Path.Combine(segments);
    }

    public static bool IsRegularFile(TarEntryType entryType) =>
        entryType is TarEntryType.RegularFile or TarEntryType.V7RegularFile or TarEntryType.ContiguousFile;
}
