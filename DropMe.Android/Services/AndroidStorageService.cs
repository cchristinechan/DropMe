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
    private AndroidNet.Uri? _downloadsFolder;
    public async Task PickDownloadsFolderAsync(Visual? visual) {
        var folders = await TopLevel.GetTopLevel(visual)?
            .StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions {
                Title = "Select download folder",
                AllowMultiple = false
            });

        if (folders.Count > 0) {
            _downloadsFolder = AndroidNet.Uri.Parse(folders[0].Path.ToString());
            //AndroidApplication.Context.ContentResolver.TakePersistableUriPermission(_downloadsFolder,
            //    ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
    }

    public (Stream, string)? OpenDownloadFileWriteStreamAsync(string fileName) {
        var context = AndroidApplication.Context;
        // Use internal files by default, maybe later change this to external
        var folder = _downloadsFolder is not null ? DocumentFile.FromTreeUri(context, _downloadsFolder) : DocumentFile.FromFile(context.FilesDir);

        var file = folder?.FindFile(fileName)
                   ?? folder?.CreateFile("application/octet-stream", fileName);
        var stream = context.ContentResolver.OpenOutputStream(file.Uri);
        if (stream is not null) {
            return (stream, file.Uri!.ToString()!);
        }
        return null;
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
        throw new NotImplementedException();
    }

    public Stream WriteConfig() {
        throw new NotImplementedException();
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
}