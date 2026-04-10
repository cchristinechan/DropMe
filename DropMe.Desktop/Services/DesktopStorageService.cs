using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DropMe.Services;

namespace DropMe.Desktop.Services;

public class DesktopStorageService : IStorageService {
    private string _downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DropMeReceived");
    private readonly string _configFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DropMe");
    private readonly string _configFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "DropMe",
        "config.json");
    public async Task PickDownloadsFolderAsync(Visual? visual) {
        var folders = await TopLevel.GetTopLevel(visual)?
            .StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                Title = "Select download folder",
                AllowMultiple = false
            });

        if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } path) {
            _downloadsFolder = path;
        }
    }

    public (Stream, string)? OpenDownloadFileWriteStream(string fileName) {
        var path = Path.Combine(_downloadsFolder, fileName);
        var stream = File.OpenWrite(path);
        return (stream, path);
    }

    public Stream ReadConfig() {
        while (true) {
            try {
                Directory.CreateDirectory(_configFolder);
                return new FileStream(_configFile, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            }
            catch (DirectoryNotFoundException) { /* retry */ }
        }
    }

    public Stream WriteConfig() {
        while (true) {
            try {
                Directory.CreateDirectory(_configFolder);
                return new FileStream(_configFile, FileMode.Create, FileAccess.Write);
            }
            catch (DirectoryNotFoundException) { /* retry */ }
        }
    }

    public string? GetDownloadDirectoryLabel() => _downloadsFolder;

    public Task<bool> TryOpenTransferTargetAsync(string target) {
        try {
            if (string.IsNullOrWhiteSpace(target))
                return Task.FromResult(false);

            if (File.Exists(target)) {
                Process.Start(new ProcessStartInfo {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{target}\"",
                    UseShellExecute = true
                });
                return Task.FromResult(true);
            }

            if (Directory.Exists(target)) {
                Process.Start(new ProcessStartInfo {
                    FileName = target,
                    UseShellExecute = true
                });
                return Task.FromResult(true);
            }

            if (Uri.TryCreate(target, UriKind.Absolute, out var uri)) {
                Process.Start(new ProcessStartInfo {
                    FileName = uri.ToString(),
                    UseShellExecute = true
                });
                return Task.FromResult(true);
            }
        }
        catch (Exception ex) {
            Console.WriteLine($"Failed to open transfer target '{target}': {ex.Message}");
        }

        return Task.FromResult(false);
    }

    public async Task<string?> ExtractDownloadedDirectoryAsync(string archivePath, string directoryName, CancellationToken cancellationToken) {
        try {
            Directory.CreateDirectory(_downloadsFolder);

            var safeName = SanitizeLeafName(directoryName);
            var destinationPath = CreateUniqueDirectoryPath(_downloadsFolder, safeName);
            Directory.CreateDirectory(destinationPath);

            await TarArchiveExtractor.ExtractToDirectoryAsync(archivePath, destinationPath, cancellationToken);
            return destinationPath;
        }
        finally {
            try {
                if (File.Exists(archivePath))
                    File.Delete(archivePath);
            }
            catch (Exception ex) {
                Console.WriteLine($"Failed to clean up temp archive '{archivePath}': {ex.Message}");
            }
        }
    }

    private static string CreateUniqueDirectoryPath(string parentDirectory, string baseName) {
        var safeBaseName = string.IsNullOrWhiteSpace(baseName) ? "Folder" : baseName;
        var candidate = Path.Combine(parentDirectory, safeBaseName);
        var suffix = 2;
        while (Directory.Exists(candidate) || File.Exists(candidate)) {
            candidate = Path.Combine(parentDirectory, $"{safeBaseName} ({suffix++})");
        }

        return candidate;
    }

    private static string SanitizeLeafName(string name) {
        var sanitized = name;
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
            sanitized = sanitized.Replace(invalidChar, '_');

        return sanitized.Trim().TrimEnd('.');
    }
}
