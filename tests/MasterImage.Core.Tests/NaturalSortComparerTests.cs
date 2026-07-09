using System.Collections.Generic;
using MasterImage.Core;
using Xunit;

namespace MasterImage.Core.Tests;

public class NaturalSortComparerTests
{
    [Fact]
    public void SortsNumericSuffixesNumerically()
    {
        var input = new List<string> { "DSC10.jpg", "DSC2.jpg", "DSC1.jpg" };
        input.Sort(new NaturalSortComparer());
        Assert.Equal(new[] { "DSC1.jpg", "DSC2.jpg", "DSC10.jpg" }, input);
    }

    [Fact]
    public void FallsBackToOrdinalForNonNumericParts()
    {
        var input = new List<string> { "banana.jpg", "apple.jpg" };
        input.Sort(new NaturalSortComparer());
        Assert.Equal(new[] { "apple.jpg", "banana.jpg" }, input);
    }

    [Fact]
    public void HandlesMixedAlphaNumericPrefixes()
    {
        var input = new List<string> { "IMG_2.jpg", "DSC_1.jpg", "IMG_10.jpg" };
        input.Sort(new NaturalSortComparer());
        Assert.Equal(new[] { "DSC_1.jpg", "IMG_2.jpg", "IMG_10.jpg" }, input);
    }
}
