using System;
using AndroidX.DocumentFile.Provider;
using System.IO;
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
}
