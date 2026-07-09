using System.Text.Json;
using System.IO;

namespace MasterImage.Core;

public sealed class MarksStore
{
    private readonly HashSet<string> _markedStems;
    private readonly string _marksPath;

    private MarksStore(string marksPath, HashSet<string> markedStems)
    {
        _marksPath = marksPath;
        _markedStems = markedStems;
    }

    public static MarksStore LoadOrCreate(string thumbnailsFolder)
    {
        string path = Path.Combine(thumbnailsFolder, "marks.json");
        if (!File.Exists(path))
        {
            return new MarksStore(path, new HashSet<string>());
        }

        string json = File.ReadAllText(path);
        var stems = JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
        return new MarksStore(path, new HashSet<string>(stems));
    }

    public bool IsMarked(string stem) => _markedStems.Contains(stem);

    public void Toggle(string stem)
    {
        if (!_markedStems.Remove(stem))
        {
            _markedStems.Add(stem);
        }
    }

    public IReadOnlyCollection<string> MarkedStems => _markedStems;

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_marksPath)!);
        string json = JsonSerializer.Serialize(_markedStems.ToList(), new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_marksPath, json);
    }
}
