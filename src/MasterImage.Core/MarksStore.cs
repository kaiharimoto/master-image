using System.Text.Json;
using System.IO;

namespace MasterImage.Core;

// Generic string-keyed persisted set. The caller supplies the key — it must be a
// collision-free identifier (e.g. a filename with extension), not a bare filename
// stem: PhotoSet.Load can emit two PhotoItems sharing a stem across extensions
// (e.g. sunset.jpg + sunset.png), which would incorrectly share mark state if
// this store were keyed on stem. See MarkKey in Task 10's MainViewModel.
public sealed class MarksStore
{
    private readonly HashSet<string> _markedKeys;
    private readonly string _marksPath;

    private MarksStore(string marksPath, HashSet<string> markedKeys)
    {
        _marksPath = marksPath;
        _markedKeys = markedKeys;
    }

    public static MarksStore LoadOrCreate(string thumbnailsFolder)
    {
        string path = Path.Combine(thumbnailsFolder, "marks.json");
        if (!File.Exists(path))
        {
            return new MarksStore(path, new HashSet<string>());
        }

        try
        {
            string json = File.ReadAllText(path);
            var keys = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            return new MarksStore(path, new HashSet<string>(keys));
        }
        catch (JsonException)
        {
            return new MarksStore(path, new HashSet<string>());
        }
    }

    public bool IsMarked(string key) => _markedKeys.Contains(key);

    public void Toggle(string key)
    {
        if (!_markedKeys.Remove(key))
        {
            _markedKeys.Add(key);
        }
    }

    public IReadOnlyCollection<string> MarkedKeys => _markedKeys;

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_marksPath)!);
        string json = JsonSerializer.Serialize(_markedKeys.ToList(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_marksPath, json);
    }
}
