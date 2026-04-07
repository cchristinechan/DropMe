using System;
using AndroidX.DocumentFile.Provider;
using System.Formats.Tar;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Android.Content;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DropMe.Services;
using AndroidApplication = Android.App.Application;
using AndroidNet = Android.Net;

namespace DropMe.Android.Services;

public class AndroidStorageService : IStorageService {
    private const string PreferencesName = "dropme_storage";
    private const string DownloadsFolderUriKey = "downloads_folder_uri";
    private AndroidNet.Uri? _downloadsFolder;
    private readonly string _configFolder;
    private readonly string _configFile;
    private readonly ISharedPreferences _preferences;
    private readonly Context _context;

    public AndroidStorageService() {
        _context = AndroidApplication.Context;
        _preferences = _context.GetSharedPreferences(PreferencesName, FileCreationMode.Private)!;
        var filesDir = _context.FilesDir?.Path ?? Path.GetTempPath();
        _configFolder = Path.Combine(filesDir, "DropMe");
        _configFile = Path.Combine(_configFolder, "config.json");
        _downloadsFolder = RestorePersistedDownloadsFolder();
    }

    public async Task PickDownloadsFolderAsync(Visual? visual) {
        var topLevel = TopLevel.GetTopLevel(visual);
        if (topLevel is null)
            return;

        var folders = await topLevel.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
            Title = "Select download folder",
            AllowMultiple = false
        });

        if (folders.Count > 0) {
            var selectedFolder = AndroidNet.Uri.Parse(folders[0].Path.ToString());
            if (selectedFolder is null)
                return;

            PersistDownloadsFolder(selectedFolder);
            _downloadsFolder = selectedFolder;
        }
    }

    public (Stream, string)? OpenDownloadFileWriteStream(string fileName) {
        Console.WriteLine("OPENING!");
        Console.WriteLine("Got context");
        // Use internal files by default, maybe later change this to external
        var filesDir = _context.FilesDir;
        var folder = _downloadsFolder is not null
            ? DocumentFile.FromTreeUri(_context, _downloadsFolder)
            : filesDir is not null
                ? DocumentFile.FromFile(filesDir)
                : null;

        var file = folder?.FindFile(fileName)
                   ?? folder?.CreateFile("application/octet-stream", fileName);
        var fileUri = file?.Uri;
        if (fileUri is null)
            return null;

        var stream = _context.ContentResolver.OpenOutputStream(fileUri);
        return stream is not null ? (stream, fileUri.ToString()!) : null;
    }

    public string? GetDownloadDirectoryLabel() {
        var path = _downloadsFolder is not null ?
            _downloadsFolder!.ToString() :
            AndroidApplication.Context.FilesDir?.Path.ToString();
        if (path is not null) {
            return NormalisePath(path);
        }

        return null;
    }

    public Stream ReadConfig() {
        while (true) {
            try {
                Directory.CreateDirectory(_configFolder);
                return new FileStream(_configFile, FileMode.OpenOrCreate, FileAccess.ReadWrite);
            }
            catch (DirectoryNotFoundException) {
                // Retry after directory creation race.
            }
        }
    }

    public Stream WriteConfig() {
        while (true) {
            try {
                Directory.CreateDirectory(_configFolder);
                return new FileStream(_configFile, FileMode.Create, FileAccess.Write);
            }
            catch (DirectoryNotFoundException) {
                // Retry after directory creation race.
            }
        }
    }

    public Task<bool> TryOpenTransferTargetAsync(string target) {
        try {
            if (string.IsNullOrWhiteSpace(target))
                return Task.FromResult(false);

            AndroidNet.Uri? uri = null;
            if (target.StartsWith("content://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("file://", StringComparison.OrdinalIgnoreCase)) {
                uri = AndroidNet.Uri.Parse(target);
            }
            else if (File.Exists(target)) {
                uri = AndroidNet.Uri.FromFile(new Java.IO.File(target));
            }

            if (uri is null)
                return Task.FromResult(false);

            var intent = new Intent(Intent.ActionView);
            intent.SetDataAndType(uri, "*/*");
            intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
            _context.StartActivity(intent);
            return Task.FromResult(true);
        }
        catch (Exception ex) {
            Console.WriteLine($"Failed to open transfer target '{target}': {ex.Message}");
            return Task.FromResult(false);
        }
    }

    public async Task<string?> ExtractDownloadedDirectoryAsync(string archivePath, string directoryName, CancellationToken cancellationToken) {
        try {
            var safeName = SanitizeLeafName(directoryName);
            if (_downloadsFolder is not null) {
                var root = DocumentFile.FromTreeUri(_context, _downloadsFolder);
                if (root is null)
                    return null;

                var targetDirectory = CreateUniqueDirectory(root, safeName);
                if (targetDirectory is null)
                    return null;

                await using var archiveStream = File.OpenRead(archivePath);
                await ExtractTarToDocumentDirectoryAsync(archiveStream, targetDirectory, cancellationToken);
                return targetDirectory.Uri?.ToString();
            }

            var basePath = _context.FilesDir?.Path ?? Path.GetTempPath();
            Directory.CreateDirectory(basePath);
            var destinationPath = CreateUniqueDirectoryPath(basePath, safeName);
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

    private string NormalisePath(string str) {
        const string treeSegment = "/tree/";
        if (str.StartsWith("content://") && str.Contains(treeSegment)) {
            int startIndex = str.IndexOf(treeSegment) + treeSegment.Length;
            string decodedPart = str.Substring(startIndex);
            return Uri.UnescapeDataString(decodedPart);
        }
        return str;
    }

    private AndroidNet.Uri? RestorePersistedDownloadsFolder() {
        var storedUri = _preferences.GetString(DownloadsFolderUriKey, null);
        if (string.IsNullOrWhiteSpace(storedUri))
            return null;

        try {
            var uri = AndroidNet.Uri.Parse(storedUri);
            if (uri is null || !HasPersistedAccess(uri))
                return null;

            var folder = DocumentFile.FromTreeUri(_context, uri);
            return folder is not null ? uri : null;
        }
        catch (Exception ex) {
            Console.WriteLine($"Failed to restore persisted downloads folder: {ex.Message}");
            ClearPersistedDownloadsFolder();
            return null;
        }
    }

    private void PersistDownloadsFolder(AndroidNet.Uri uri) {
        if (_downloadsFolder is not null && _downloadsFolder != uri) {
            try {
                _context.ContentResolver.ReleasePersistableUriPermission(
                    _downloadsFolder,
                    ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
            }
            catch (Exception ex) {
                Console.WriteLine($"Failed to release old downloads folder permission: {ex.Message}");
            }
        }

        _context.ContentResolver.TakePersistableUriPermission(
            uri,
            ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);

        using var editor = _preferences.Edit();
        editor.PutString(DownloadsFolderUriKey, uri.ToString());
        editor.Apply();
    }

    private bool HasPersistedAccess(AndroidNet.Uri uri) {
        foreach (var permission in _context.ContentResolver.PersistedUriPermissions) {
            if (permission.Uri?.ToString() == uri.ToString() &&
                permission.IsReadPermission &&
                permission.IsWritePermission) {
                return true;
            }
        }

        ClearPersistedDownloadsFolder();
        return false;
    }

    private void ClearPersistedDownloadsFolder() {
        using var editor = _preferences.Edit();
        editor.Remove(DownloadsFolderUriKey);
        editor.Apply();
    }

    private async Task ExtractTarToDocumentDirectoryAsync(Stream archiveStream, DocumentFile rootDirectory, CancellationToken cancellationToken) {
        using var tarReader = new TarReader(archiveStream, leaveOpen: true);
        while (tarReader.GetNextEntry(copyData: false) is { } entry) {
            cancellationToken.ThrowIfCancellationRequested();

            var normalizedPath = TarArchiveExtractor.NormalizeTarEntryPath(entry.Name);
            if (string.IsNullOrWhiteSpace(normalizedPath))
                continue;

            var segments = normalizedPath.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length == 0)
                continue;

            if (entry.EntryType is TarEntryType.Directory) {
                EnsureDirectory(rootDirectory, segments);
                continue;
            }

            if (!TarArchiveExtractor.IsRegularFile(entry.EntryType) || entry.DataStream is null)
                continue;

            var parent = segments.Length > 1
                ? EnsureDirectory(rootDirectory, segments[..^1])
                : rootDirectory;
            if (parent is null)
                throw new IOException("Failed to create directory while extracting folder transfer.");

            var fileName = SanitizeLeafName(segments[^1]);
            var existing = parent.FindFile(fileName);
            existing?.Delete();

            var outputFile = parent.CreateFile("application/octet-stream", fileName);
            if (outputFile?.Uri is null)
                throw new IOException($"Failed to create output file '{fileName}'.");

            await using var output = _context.ContentResolver.OpenOutputStream(outputFile.Uri);
            if (output is null)
                throw new IOException($"Failed to open output file '{fileName}' for writing.");

            await entry.DataStream.CopyToAsync(output, cancellationToken);
        }
    }

    private DocumentFile? EnsureDirectory(DocumentFile root, string[] segments) {
        var current = root;
        foreach (var rawSegment in segments) {
            var segment = SanitizeLeafName(rawSegment);
            if (string.IsNullOrWhiteSpace(segment))
                continue;

            var next = current.FindFile(segment);
            if (next is null) {
                next = current.CreateDirectory(segment);
            }
            else if (!next.IsDirectory) {
                next.Delete();
                next = current.CreateDirectory(segment);
            }

            if (next is null)
                return null;

            current = next;
        }

        return current;
    }

    private static DocumentFile? CreateUniqueDirectory(DocumentFile parent, string baseName) {
        var safeBaseName = string.IsNullOrWhiteSpace(baseName) ? "Folder" : baseName;
        var candidateName = safeBaseName;
        var suffix = 2;

        while (parent.FindFile(candidateName) is not null) {
            candidateName = $"{safeBaseName} ({suffix++})";
        }

        return parent.CreateDirectory(candidateName);
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

        sanitized = sanitized.Replace('/', '_').Replace('\\', '_').Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(sanitized) ? "Folder" : sanitized;
    }
}
