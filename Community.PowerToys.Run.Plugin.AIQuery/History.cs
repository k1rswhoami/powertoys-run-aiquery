using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Community.PowerToys.Run.Plugin.AIQuery;

public record HistoryEntry(
    string Question,
    string Answer,
    string Model,
    DateTime Timestamp);

public class HistoryManager
{
    private const int MaxEntries = 100;
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    private readonly string _filePath;
    private readonly object _lock = new();
    private List<HistoryEntry> _entries = [];

    public HistoryManager(string pluginDir)
    {
        _filePath = Path.Combine(pluginDir, "history.json");
        Load();
    }

    public IReadOnlyList<HistoryEntry> Entries
    {
        get { lock (_lock) return _entries.AsReadOnly(); }
    }

    public void Add(string question, string answer, string model)
    {
        lock (_lock)
        {
            // Remove duplicate question if it exists
            _entries.RemoveAll(e => e.Question.Equals(question, StringComparison.OrdinalIgnoreCase));
            _entries.Insert(0, new HistoryEntry(question, answer, model, DateTime.Now));
            if (_entries.Count > MaxEntries)
                _entries = _entries[..MaxEntries];
        }
        Save();
    }

    public void Remove(HistoryEntry entry)
    {
        lock (_lock) _entries.Remove(entry);
        Save();
    }

    public void Clear()
    {
        lock (_lock) _entries.Clear();
        Save();
    }

    public List<HistoryEntry> Search(string keyword)
    {
        lock (_lock)
            return _entries
                .Where(e => e.Question.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                            e.Answer.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                .ToList();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            _entries = JsonSerializer.Deserialize<List<HistoryEntry>>(json, JsonOpts) ?? [];
        }
        catch { _entries = []; }
    }

    private void Save()
    {
        try
        {
            List<HistoryEntry> snapshot;
            lock (_lock) snapshot = [.. _entries];
            File.WriteAllText(_filePath, JsonSerializer.Serialize(snapshot, JsonOpts));
        }
        catch { }
    }
}
