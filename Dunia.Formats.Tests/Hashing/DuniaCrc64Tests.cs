using Dunia.Formats.Hashing;

namespace Dunia.Formats.Tests.Hashing;

public sealed class DuniaCrc64Tests
{
    [Theory]
    [InlineData("", 0x0000000000000000UL)]
    [InlineData("test", 0x47838D37C0000000UL)]
    [InlineData("engine\\settings\\defaultrenderconfig.xml", 0x9B0BAE4645EFEAF5UL)]
    public void ComputeMatchesReferenceVectors(string value, ulong expected)
    {
        Assert.Equal(expected, DuniaCrc64.Compute(value));
    }

    [Fact]
    public void PathHashNormalizesCaseAndSeparators()
    {
        ulong expected = DuniaPathHash.Compute("Graphics\\_Common\\Textures\\Example.XBT");

        Assert.Equal(expected, DuniaPathHash.Compute("graphics/_common/textures/example.xbt"));
    }
}
