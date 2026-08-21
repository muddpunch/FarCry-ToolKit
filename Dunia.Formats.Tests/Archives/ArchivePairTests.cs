using Dunia.Formats.Archives;

namespace Dunia.Formats.Tests.Archives;

public sealed class ArchivePairTests
{
    [Fact]
    public void FromIndexUsesAdjacentDatFile()
    {
        string fatPath = Path.Combine("archives", "common.fat");

        ArchivePair pair = ArchivePair.FromIndex(fatPath);

        Assert.Equal(Path.GetFullPath(fatPath), pair.FatPath);
        Assert.Equal(Path.GetFullPath(Path.Combine("archives", "common.dat")), pair.DatPath);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void FromIndexRejectsEmptyPath(string fatPath)
    {
        Assert.Throws<ArgumentException>(() => ArchivePair.FromIndex(fatPath));
    }
}
