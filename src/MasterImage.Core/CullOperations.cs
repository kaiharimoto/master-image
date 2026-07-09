using System.IO;

namespace MasterImage.Core;

public static class CullOperations
{
    public sealed record MoveResult(int MovedFileCount, IReadOnlyList<string> Failures);

    public static MoveResult MoveMarkedToSelectedFolder(string folderPath, IEnumerable<PhotoItem> markedItems)
    {
        string selectedFolder = Path.Combine(folderPath, "selected");
        Directory.CreateDirectory(selectedFolder);

        int movedCount = 0;
        var failures = new List<string>();

        foreach (var item in markedItems)
        {
            foreach (var filePath in item.FilePaths)
            {
                string destination = Path.Combine(selectedFolder, Path.GetFileName(filePath));
                try
                {
                    if (File.Exists(destination))
                    {
                        failures.Add($"{filePath} (already exists in selected/)");
                        continue;
                    }

                    File.Move(filePath, destination);
                    movedCount++;
                }
                catch (IOException ex)
                {
                    failures.Add($"{filePath} ({ex.Message})");
                }
                catch (UnauthorizedAccessException ex)
                {
                    failures.Add($"{filePath} ({ex.Message})");
                }
            }
        }

        return new MoveResult(movedCount, failures);
    }
}
