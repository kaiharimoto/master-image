using System.Text.RegularExpressions;

namespace MasterImage.Core;

public sealed class NaturalSortComparer : IComparer<string>
{
    private static readonly Regex ChunkPattern = new(@"\d+|\D+", RegexOptions.Compiled);

    public int Compare(string? x, string? y)
    {
        if (x is null || y is null)
        {
            return string.Compare(x, y, StringComparison.Ordinal);
        }

        var xChunks = ChunkPattern.Matches(x);
        var yChunks = ChunkPattern.Matches(y);
        int count = Math.Min(xChunks.Count, yChunks.Count);

        for (int i = 0; i < count; i++)
        {
            string xChunk = xChunks[i].Value;
            string yChunk = yChunks[i].Value;

            bool xIsDigits = char.IsDigit(xChunk[0]);
            bool yIsDigits = char.IsDigit(yChunk[0]);

            if (xIsDigits && yIsDigits &&
                long.TryParse(xChunk, out long xNum) &&
                long.TryParse(yChunk, out long yNum) &&
                xNum != yNum)
            {
                return xNum.CompareTo(yNum);
            }

            int chunkCompare = string.Compare(xChunk, yChunk, StringComparison.OrdinalIgnoreCase);
            if (chunkCompare != 0)
            {
                return chunkCompare;
            }
        }

        return xChunks.Count.CompareTo(yChunks.Count);
    }
}
