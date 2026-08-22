using System.Buffers.Binary;
using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbRoundTripVerifierTests
{
    [Fact]
    public void VerifyReportsByteExactAndRestoresPosition()
    {
        byte[] source = new byte[FcbReader.HeaderSize + 6];
        BinaryPrimitives.WriteUInt32LittleEndian(source, FcbReader.Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(source.AsSpan(4), FcbReader.Version);
        source[FcbReader.HeaderSize] = 0;
        source[FcbReader.HeaderSize + 1] = 1;
        using var input = new MemoryStream(source, false) { Position = 4 };

        FcbRoundTripVerificationResult result = FcbRoundTripVerifier.Verify(input);

        Assert.True(result.IsByteExact);
        Assert.Equal(source.Length, result.Length);
        Assert.Equal(result.SourceSha256, result.OutputSha256);
        Assert.Equal(4, input.Position);
    }
}
