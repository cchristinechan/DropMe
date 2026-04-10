using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;

namespace DropMe.Services;

/// <summary>
/// Interface for storage services required to be implemented by supported platforms.
/// </summary>
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
    public (Stream, string)? OpenDownloadFileWriteStream(string fileName);
    /// <summary>
    /// Gets a user friendly label of the downloads folder. This may or may not be a valid path so should not be used
    /// to open files.
    /// </summary>
    /// <returns>A user friendly label of the downloads folder.</returns>
    public string? GetDownloadDirectoryLabel();
    /// <summary>
    /// Attempts to open a previously stored transfer target in the platform shell.
    /// </summary>
    /// <param name="target">A local path or persisted content URI.</param>
    /// <returns>True if the platform attempted to open it, otherwise false.</returns>
    public Task<bool> TryOpenTransferTargetAsync(string target);
    /// <summary>
    /// Extracts a received tar archive into the configured downloads area as a directory.
    /// </summary>
    /// <param name="archivePath">Local temporary path to the received tar archive.</param>
    /// <param name="directoryName">Requested directory name from the sender.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The saved directory path or URI if extraction succeeded; otherwise null.</returns>
    public Task<string?> ExtractDownloadedDirectoryAsync(string archivePath, string directoryName, CancellationToken cancellationToken);
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
