namespace MasterImage.Core;

public sealed class PhotoItem
{
    public PhotoItem(string stem, IReadOnlyList<string> filePaths)
    {
        Stem = stem;
        FilePaths = filePaths;
    }

    public string Stem { get; }
    public IReadOnlyList<string> FilePaths { get; }
    public string PrimaryFilePath => FilePaths[0];
}
