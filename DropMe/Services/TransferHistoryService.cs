using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DropMe.Services;

public enum TransferFileCategory {
    Image,
    Video,
    Document,
    Download
}

public sealed record TransferHistoryEntry(
    Guid TransferId,
    string FileName,
    string LocationLabel,
    string? OpenTarget,
    DateTime CompletedAt,
    string DirectionLabel,
    long FileSizeBytes,
    bool Successful) {
    public string TimestampLabel => CompletedAt.ToLocalTime().ToString("dd MMM yyyy, HH:mm");

    public TransferFileCategory Category {
        get {
            var extension = Path.GetExtension(FileName).ToLowerInvariant();
            return extension switch {
                ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".heic" => TransferFileCategory.Image,
                ".mp4" or ".mov" or ".avi" or ".mkv" or ".webm" or ".m4v" => TransferFileCategory.Video,
                ".pdf" or ".doc" or ".docx" or ".txt" or ".rtf" or ".xls" or ".xlsx" or ".ppt" or ".pptx" or ".csv" or ".zip" => TransferFileCategory.Document,
                _ => TransferFileCategory.Download
            };
        }
    }

    public string IconPathData => Category switch {
        TransferFileCategory.Image => "M21,19V5A2,2 0 0,0 19,3H5A2,2 0 0,0 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19M8.5,13.5L11,16.5L14.5,12L19,18H5L8.5,13.5M14.5,9A1.5,1.5 0 0,1 16,10.5A1.5,1.5 0 0,1 14.5,12A1.5,1.5 0 0,1 13,10.5A1.5,1.5 0 0,1 14.5,9Z",
        TransferFileCategory.Video => "M17,10.5V7A2,2 0 0,0 15,5H5A2,2 0 0,0 3,7V17A2,2 0 0,0 5,19H15A2,2 0 0,0 17,17V13.5L21,17V7L17,10.5Z",
        TransferFileCategory.Document => "M14,2H6A2,2 0 0,0 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2M14,3.5L18.5,8H14V3.5Z",
        _ => "M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z"
    };

    public bool CanOpenTarget => !string.IsNullOrWhiteSpace(OpenTarget);
}

public sealed class TransferHistoryService(ConfigService configService) {
    private const string HistoryConfigKey = "transfer_history";

    public ObservableCollection<TransferHistoryEntry> Entries { get; } = [];

    public void Load() {
        Entries.Clear();
        foreach (var entry in ReadEntries()) {
            Entries.Add(entry);
        }
    }

    public void AddEntry(TransferHistoryEntry entry) {
        Entries.Insert(0, entry);
        Save();
    }

    private IReadOnlyList<TransferHistoryEntry> ReadEntries() {
        var raw = configService.GetValue(HistoryConfigKey);
        if (string.IsNullOrWhiteSpace(raw))
            return [];

        try {
            return JsonSerializer.Deserialize<List<TransferHistoryEntry>>(raw) ?? [];
        }
        catch (Exception ex) {
            Console.WriteLine($"Failed to read transfer history: {ex.Message}");
            return [];
        }
    }

    private void Save() {
        var serialised = JsonSerializer.Serialize(Entries.ToList());
        configService.InsertKVPair(HistoryConfigKey, serialised);
    }
}
