using System.Buffers.Binary;
using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbValueProjectorTests
{
    [Fact]
    public void ProjectRecognizesStructurallyValidAsciiZ()
    {
        var field = new FcbField(1, "Hello\0"u8.ToArray(), 0, null);

        FcbValueProjection projection = FcbValueProjector.Project(field);

        FcbValueCandidate candidate = Assert.Single(projection.Candidates);
        Assert.Equal(FcbValueKind.AsciiNullTerminated, candidate.Kind);
        Assert.Equal(FcbValueEvidence.Structural, candidate.Evidence);
        Assert.Equal("Hello", candidate.Value);
        Assert.Equal("48656C6C6F00", projection.RawHex);
    }

    [Fact]
    public void ProjectKeepsAllFourByteInterpretationsAmbiguous()
    {
        byte[] data = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(data, BitConverter.SingleToInt32Bits(1.0F));
        var field = new FcbField(1, data, 0, null);

        FcbValueProjection projection = FcbValueProjector.Project(field);

        Assert.True(projection.IsAmbiguous);
        Assert.Collection(
            projection.Candidates,
            candidate => Assert.Equal(FcbValueKind.Signed32Bit, candidate.Kind),
            candidate => Assert.Equal(FcbValueKind.Unsigned32Bit, candidate.Kind),
            candidate =>
            {
                Assert.Equal(FcbValueKind.Ieee754Binary32, candidate.Kind);
                Assert.Equal("1", candidate.Value);
            });
    }

    [Fact]
    public void ProjectRecognizesBooleanOnlyForCanonicalByteValues()
    {
        var trueField = new FcbField(1, [1], 0, null);
        var otherField = new FcbField(1, [2], 0, null);

        Assert.Contains(FcbValueProjector.Project(trueField).Candidates, item => item.Kind == FcbValueKind.Boolean);
        Assert.DoesNotContain(FcbValueProjector.Project(otherField).Candidates, item => item.Kind == FcbValueKind.Boolean);
    }
}
