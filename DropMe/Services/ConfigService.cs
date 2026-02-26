using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;

namespace DropMe.Services;

public class ConfigService(IStorageService storageService) {
    // Returns the previous value if applicable
    public string? InsertKVPair(string key, string value) {
        var table = GetHashTable();
        var previous = table.GetValueOrDefault(key);
        table[key] = value;

        using var ostream = storageService.WriteConfig();
        using var writer = new System.IO.StreamWriter(ostream);
        
        var serialised = System.Text.Json.JsonSerializer.Serialize(table);
        
        writer.Write(serialised);
        writer.Flush();
        
        return previous;
    }
    
    public async Task<string?> InsertKVPairAsync(string key, string value) {
        var table = await GetHashTableAsync();
        var previous = table.GetValueOrDefault(key);
        table[key] = value;

        await using var ostream = storageService.WriteConfig();
        await using var writer = new System.IO.StreamWriter(ostream);
        
        var serialised = System.Text.Json.JsonSerializer.Serialize(table);
        
        await writer.WriteAsync(serialised);
        await writer.FlushAsync();
        
        return previous;
    }

    public string? GetValue(string key) => GetHashTable().GetValueOrDefault(key);

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

    private Dictionary<string, string> GetHashTable() {
        return _hashtable ??= ReadConfigFile();
    }

    private async Task<Dictionary<string, string>> GetHashTableAsync() {
        return _hashtable ??= await ReadConfigFileAsync();
    }
    
    private Dictionary<string, string>? _hashtable;
}