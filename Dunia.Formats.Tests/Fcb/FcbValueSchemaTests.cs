using System.Buffers.Binary;
using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbValueSchemaTests
{
    [Fact]
    public void LoadAndProjectResolveCompatibleCodec()
    {
        using var input = new StringReader("11223344 55667788 Signed32Bit\n");
        FcbValueSchema schema = FcbValueSchema.Load(input);
        byte[] data = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(data, -42);
        var field = new FcbField(0x55667788, data, 0, null);

        FcbTypedValueProjection projection = FcbTypedValueProjector.Project(0x11223344, field, schema);

        Assert.Equal(FcbTypedValueStatus.Resolved, projection.Status);
        Assert.Equal(FcbValueKind.Signed32Bit, projection.Codec);
        Assert.Equal("-42", projection.Value);
    }

    [Fact]
    public void ProjectReportsMissingAndIncompatibleSchema()
    {
        using var input = new StringReader("11223344 55667788 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(input);
        var incompatible = new FcbField(0x55667788, [2], 0, null);
        var missing = new FcbField(0x99, [1], 0, null);

        Assert.Equal(
            FcbTypedValueStatus.Incompatible,
            FcbTypedValueProjector.Project(0x11223344, incompatible, schema).Status);
        Assert.Equal(
            FcbTypedValueStatus.MissingSchema,
            FcbTypedValueProjector.Project(0x11223344, missing, schema).Status);
    }

    [Fact]
    public void LoadRejectsDuplicateKeys()
    {
        using var input = new StringReader(
            "11223344 55667788 Signed32Bit\n11223344 55667788 Unsigned32Bit\n");

        Assert.Throws<InvalidDataException>(() => FcbValueSchema.Load(input));
    }

    [Theory]
    [InlineData("1122334 55667788 Signed32Bit")]
    [InlineData("11223344 55667788 UnknownCodec")]
    [InlineData("11223344 55667788")]
    public void LoadRejectsInvalidEntries(string line)
    {
        using var input = new StringReader(line);

        Assert.Throws<InvalidDataException>(() => FcbValueSchema.Load(input));
    }
}
