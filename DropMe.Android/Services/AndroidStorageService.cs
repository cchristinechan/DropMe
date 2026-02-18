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
            AndroidApplication.Context.ContentResolver.TakePersistableUriPermission(_downloadsFolder,
                ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission);
        }
    }

    public Stream? OpenDownloadFileWriteStreamAsync(string fileName) {
        var context = AndroidApplication.Context;
        var folder = DocumentFile.FromTreeUri(context, _downloadsFolder);
        
        var file = folder?.FindFile(fileName)
                   ?? folder?.CreateFile("application/octet-stream", fileName);
        return context.ContentResolver.OpenOutputStream(file.Uri);
    }
}