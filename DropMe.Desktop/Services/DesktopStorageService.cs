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

    public (Stream, string)? OpenDownloadFileWriteStreamAsync(string fileName) {
        var path = Path.Combine(_downloadsFolder, fileName);
        var stream = File.OpenWrite(path);
        return (stream, path);
    }

    public Stream ReadConfig() {
        throw new NotImplementedException();
    }

    public Stream WriteConfig() {
        throw new NotImplementedException();
    }
    
    public string? GetDownloadDirectoryLabel() => _downloadsFolder;
}