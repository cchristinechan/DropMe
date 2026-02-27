using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace DropMe.Services;

/// <summary>
/// Service to store persistent configuration data as key value pairs.
/// A key can be associated with a maximum of 1 value.
/// </summary>
/// <param name="storageService">The storage service used to retrieve the backing data streams</param>
public class ConfigService(IStorageService storageService) {
    /// <summary>
    /// Insert a key value pair into persistent configuration for the app.
    /// </summary>
    /// <param name="key">The key that will be used to later retrieve the associated value.</param>
    /// <param name="value">The value associated with this key.</param>
    /// <returns>The previous value associated with this key, null if none was previously associated with it.</returns>
    /// <exception cref="System.IO.IOException">
    /// Thrown if there is an IO exception writing to the backing storage. Retry if this occurs.
    /// </exception>
    public string? InsertKVPair(string key, string value) {
        var table = GetHashTable();
        var previous = table.GetValueOrDefault(key);
        table[key] = value;

        WriteConfig(table);

        return previous;
    }

    /// <summary>
    /// Insert a key value pair into persistent configuration for the app.
    /// </summary>
    /// <param name="key">The key that will be used to later retrieve the associated value.</param>
    /// <param name="value">The value associated with this key.</param>
    /// <returns>The previous value associated with this key, null if none was previously associated with it.</returns>
    /// <exception cref="System.IO.IOException">
    /// Thrown if there is an IO exception writing to the backing storage. Retry if this occurs.
    /// </exception>
    public async Task<string?> InsertKVPairAsync(string key, string value) {
        var table = await GetHashTableAsync();
        var previous = table.GetValueOrDefault(key);
        table[key] = value;

        await WriteConfigAsync(table);

        return previous;
    }

    /// <summary>
    /// Removes a key value pair from the config.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>True if the key existed in the config, otherwise false.</returns>
    public bool RemoveKVPair(string key) {
        var table = GetHashTable();
        var val = table.Remove(key);
        WriteConfig(table);
        return val;
    }

    /// <summary>
    /// Removes a key value pair from the config.
    /// </summary>
    /// <param name="key">The key to remove.</param>
    /// <returns>True if the key existed in the config, otherwise false.</returns>
    public async Task<bool> RemoveKVPairAsync(string key) {
        var table = await GetHashTableAsync();
        var val = table.Remove(key);
        await WriteConfigAsync(table);
        return val;
    }

    /// <summary>
    /// Removes all key value pairs from the config.
    /// </summary>
    public void RemoveAllKVPairs() {
        var table = GetHashTable();
        foreach (var key in table.Keys) {
            table.Remove(key);
        }
        WriteConfig(table);
    }

    /// <summary>
    /// Removes all key value pairs from the config.
    /// </summary>
    public async Task RemoveAllKVPairsAsync() {
        var table = await GetHashTableAsync();
        foreach (var key in table.Keys) {
            table.Remove(key);
        }
        await WriteConfigAsync(table);
    }

    /// <summary>
    /// Gives an iterator over all key value pairs in the config.
    /// </summary>
    /// <returns>An iterator over all key value pairs in the config.</returns>
    public IEnumerable<KeyValuePair<string, string>> GetAllKVPairs() {
        return GetHashTable()
            .Keys
            .Select(key => new KeyValuePair<string, string>(key, GetHashTable()[key]));
    }

    /// <summary>
    /// Gets the value associated with a key.
    /// </summary>
    /// <param name="key">The key to retrieve the associated value with.</param>
    /// <returns>The associated value, null if it doesn't exist.</returns>
    public string? GetValue(string key) => GetHashTable().GetValueOrDefault(key);

    /// <summary>
    /// Gets the value associated with a key.
    /// </summary>
    /// <param name="key">The key to retrieve the associated value with.</param>
    /// <returns>The associated value, null if it doesn't exist.</returns>
    public async Task<string?> GetValueAsync(string key) {
        var table = await GetHashTableAsync();
        return table.GetValueOrDefault(key);
    }

    private Dictionary<string, string> ReadConfigFile() {
        using var istream = storageService.ReadConfig();
        try {
            var table = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(istream);
            return table ?? new Dictionary<string, string>();
        }
        catch (Exception) {
            // Error deserializing the config so assume it's corrupted and create an empty table
            return new Dictionary<string, string>();
        }
    }

    private async Task<Dictionary<string, string>> ReadConfigFileAsync() {
        await using var istream = storageService.ReadConfig();
        try {
            var table = await JsonSerializer.DeserializeAsync<Dictionary<string, string>>(istream);
            return table ?? new Dictionary<string, string>();
        }
        catch (Exception) {
            // Error deserializing the config so assume it's corrupted and create an empty table
            return new Dictionary<string, string>();
        }
    }

    private void WriteConfig(Dictionary<string, string> table) {
        using var ostream = storageService.WriteConfig();
        using var writer = new System.IO.StreamWriter(ostream);

        var serialised = System.Text.Json.JsonSerializer.Serialize(table);

        writer.Write(serialised);
        writer.Flush();
    }

    private async Task WriteConfigAsync(Dictionary<string, string> table) {
        await using var ostream = storageService.WriteConfig();
        await using var writer = new System.IO.StreamWriter(ostream);

        var serialised = System.Text.Json.JsonSerializer.Serialize(table);

        await writer.WriteAsync(serialised);
        await writer.FlushAsync();
    }

    private Dictionary<string, string> GetHashTable() {
        return _hashtable ??= ReadConfigFile();
    }

    private async Task<Dictionary<string, string>> GetHashTableAsync() {
        return _hashtable ??= await ReadConfigFileAsync();
    }

    private Dictionary<string, string>? _hashtable;
}