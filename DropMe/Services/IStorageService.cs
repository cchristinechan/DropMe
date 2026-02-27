using System.IO;
using System.Threading.Tasks;
using Avalonia;

namespace DropMe.Services;

public interface IStorageService {
    /// <summary>
    /// Opens a dialogue for the user to pick a downloads folder for dropme to use.
    /// </summary>
    /// <param name="visual">A visual for the dialogue to be linked to.</param>
    public Task PickDownloadsFolderAsync(Visual? visual);
    /// <summary>
    /// Opens a stream with write permissions into the set downloads folder.
    /// </summary>
    /// <param name="fileName">The name of the file within the downloads folder to be opened.</param>
    /// <returns>The stream and associated path of the file opened.</returns>
    public (Stream, string)? OpenDownloadFileWriteStreamAsync(string fileName);
    /// <summary>
    /// Gets a user friendly label of the downloads folder. This may or may not be a valid path so should not be used
    /// to open files.
    /// </summary>
    /// <returns>A user friendly label of the downloads folder.</returns>
    public string? GetDownloadDirectoryLabel();
    /// <summary>
    /// Opens a stream for reading to the backing storage of the config.
    /// </summary>
    /// <returns>A stream with read permissions for the backing storage of the config.</returns>
    public Stream ReadConfig();
    /// <summary>
    /// Truncates any existing config file and returns a writable stream to this new empty file.
    /// </summary>
    /// <returns>A writable stream to an empty config file.</returns>
    public Stream WriteConfig();
}