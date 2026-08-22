using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbValueEncoderTests
{
    [Theory]
    [InlineData(FcbValueKind.AsciiNullTerminated, "Test", "5465737400")]
    [InlineData(FcbValueKind.Boolean, "true", "01")]
    [InlineData(FcbValueKind.Signed16Bit, "-2", "FEFF")]
    [InlineData(FcbValueKind.Unsigned32Bit, "305419896", "78563412")]
    [InlineData(FcbValueKind.Crc32Hash, "12345678", "78563412")]
    [InlineData(FcbValueKind.Crc64Hash, "0123456789ABCDEF", "EFCDAB8967452301")]
    [InlineData(FcbValueKind.Ieee754Binary32, "1.5", "0000C03F")]
    [InlineData(FcbValueKind.Vector2Binary32, "[1,-2]", "0000803F000000C0")]
    public void EncodeProducesCanonicalLittleEndianBytes(FcbValueKind codec, string value, string expected)
    {
        Assert.Equal(expected, Convert.ToHexString(FcbValueEncoder.Encode(codec, value)));
    }

    [Theory]
    [InlineData(FcbValueKind.Boolean, "yes")]
    [InlineData(FcbValueKind.AsciiNullTerminated, "zażółć")]
    [InlineData(FcbValueKind.Crc32Hash, "1234")]
    [InlineData(FcbValueKind.Ieee754Binary64, "NaN")]
    [InlineData(FcbValueKind.Vector3Binary32, "1,2")]
    public void EncodeRejectsNonCanonicalValues(FcbValueKind codec, string value)
    {
        Assert.Throws<FormatException>(() => FcbValueEncoder.Encode(codec, value));
    }
}
