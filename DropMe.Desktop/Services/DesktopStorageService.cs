using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DropMe.Services;

namespace DropMe.Desktop.Services;

public class DesktopStorageService : IStorageService {
    private string _downloadsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DropMeReceived");
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

    public Stream? OpenDownloadFileWriteStreamAsync(string fileName)
        => File.OpenWrite(Path.Combine(_downloadsFolder, fileName));
}