using System.Text;
using Dunia.Formats.Hashing;

namespace Dunia.Formats.Tests.Hashing;

public sealed class DuniaCrc32Tests
{
    [Fact]
    public void ComputeMatchesCrc32IsoHdlcCheckValue()
    {
        Assert.Equal(0xCBF43926U, DuniaCrc32.Compute("123456789"));
        Assert.Equal(0xCBF43926U, DuniaCrc32.Compute(Encoding.ASCII.GetBytes("123456789")));
    }

    [Fact]
    public void ComputePreservesCaseAndUsesLowCharacterByte()
    {
        Assert.NotEqual(DuniaCrc32.Compute("Test"), DuniaCrc32.Compute("test"));
        Assert.Equal(DuniaCrc32.Compute(new byte[] { 0x34 }), DuniaCrc32.Compute("\u1234"));
    }
}
