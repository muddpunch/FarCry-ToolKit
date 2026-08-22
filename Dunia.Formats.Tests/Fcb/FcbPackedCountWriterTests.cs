using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbPackedCountWriterTests
{
    [Theory]
    [InlineData(0U, false, "00")]
    [InlineData(0xFDU, false, "FD")]
    [InlineData(0xFEU, false, "FFFE000000")]
    [InlineData(0x12345678U, true, "FE78563412")]
    public void WriteUsesCanonicalEncoding(uint value, bool isOffset, string expectedHex)
    {
        using var output = new MemoryStream();

        FcbPackedCountWriter.Write(output, new(value, isOffset));

        Assert.Equal(Convert.FromHexString(expectedHex), output.ToArray());
    }
}
