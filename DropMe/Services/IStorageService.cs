using System.IO;
using System.Threading.Tasks;
using Avalonia;

namespace DropMe.Services;

public interface IStorageService {
    // Opens a dialog for the user to pick a downloads folder
    public Task PickDownloadsFolderAsync(Visual? visual);
    // Opens a stream in the downloads folder to write a file to
    public (Stream, string)? OpenDownloadFileWriteStreamAsync(string fileName);
    // Do NOT use this to create/open files, use OpenDownloadFileWriteStream to directly open a stream
    // Maybe change to be an onDownloadDirectoryChanged event
    public string? GetDownloadDirectoryLabel();
}