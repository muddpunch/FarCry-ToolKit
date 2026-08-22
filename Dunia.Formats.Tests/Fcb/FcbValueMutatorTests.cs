using System.Buffers.Binary;
using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbValueMutatorTests
{
    [Fact]
    public void ReplaceInlineFieldReturnsVerifiedReparsedDocument()
    {
        FcbDocument source = ReadDocument([1]);
        FcbValueSchema schema = ReadSchema("00000010 00000020 Boolean\n");

        FcbValueMutationResult result = FcbValueMutator.ReplaceInlineField(
            source,
            source.Root.Fields[0],
            [0],
            schema);

        Assert.Equal(FcbValueKind.Boolean, result.Codec);
        Assert.Equal<byte>([0], result.Document.Root.Fields[0].Data.ToArray());
        Assert.True(FcbValueSchemaCoverageAnalyzer.Analyze(result.Document, schema).IsComplete);
        using var verificationInput = new MemoryStream(result.Data.ToArray(), false);
        Assert.True(FcbRoundTripVerifier.Verify(verificationInput).IsByteExact);
    }

    [Fact]
    public void ReplaceInlineFieldRejectsIncompleteSourceSchema()
    {
        FcbDocument source = ReadDocument([1]);
        FcbValueSchema schema = ReadSchema(string.Empty);

        Assert.Throws<InvalidOperationException>(() =>
            FcbValueMutator.ReplaceInlineField(source, source.Root.Fields[0], [0], schema));
    }

    [Fact]
    public void ReplaceInlineFieldRejectsIncompatibleReplacement()
    {
        FcbDocument source = ReadDocument([1]);
        FcbValueSchema schema = ReadSchema("00000010 00000020 Boolean\n");

        Assert.Throws<ArgumentException>(() =>
            FcbValueMutator.ReplaceInlineField(source, source.Root.Fields[0], [2], schema));
    }

    [Fact]
    public void ReplaceInlineFieldUpdatesBackwardReferences()
    {
        using var body = new MemoryStream();
        body.WriteByte(0);
        WriteUInt32(body, 0x10);
        body.WriteByte(2);
        WriteUInt32(body, 0x20);
        long inlineOffset = body.Position + FcbReader.HeaderSize;
        body.WriteByte(1);
        body.WriteByte(1);
        WriteUInt32(body, 0x21);
        long referenceOffset = body.Position + FcbReader.HeaderSize;
        body.WriteByte(0xFE);
        WriteUInt32(body, checked((uint)(referenceOffset - inlineOffset)));
        FcbDocument source = ReadDocumentBody(body.ToArray());
        FcbValueSchema schema = ReadSchema(
            "00000010 00000020 Boolean\n00000010 00000021 Boolean\n");

        FcbValueMutationResult result = FcbValueMutator.ReplaceInlineField(
            source,
            source.Root.Fields[0],
            [0],
            schema);

        Assert.Equal<byte>([0], result.Document.Root.Fields[0].Data.ToArray());
        Assert.Equal<byte>([0], result.Document.Root.Fields[1].Data.ToArray());
        Assert.Same(result.Document.Root.Fields[0], result.Document.Root.Fields[1].ReferenceTarget);
    }

    [Fact]
    public void ReplaceInlineFieldsAppliesAllChangesInOneVerifiedSerialization()
    {
        FcbDocument source = ReadDocumentWithFields(
            (0x20, new byte[] { 1 }),
            (0x21, new byte[] { 0 }));
        FcbValueSchema schema = ReadSchema(
            "00000010 00000020 Boolean\n00000010 00000021 Boolean\n");

        FcbValueBatchMutationResult result = FcbValueMutator.ReplaceInlineFields(
            source,
            [
                new(source.Root.Fields[0], new byte[] { 0 }),
                new(source.Root.Fields[1], new byte[] { 1 }),
            ],
            schema);

        Assert.Equal([FcbValueKind.Boolean, FcbValueKind.Boolean], result.Codecs);
        Assert.Equal<byte>([0], result.Document.Root.Fields[0].Data.ToArray());
        Assert.Equal<byte>([1], result.Document.Root.Fields[1].Data.ToArray());
        Assert.Equal<byte>([1], source.Root.Fields[0].Data.ToArray());
        Assert.Equal<byte>([0], source.Root.Fields[1].Data.ToArray());
        using var verificationInput = new MemoryStream(result.Data.ToArray(), false);
        Assert.True(FcbRoundTripVerifier.Verify(verificationInput).IsByteExact);
    }

    [Fact]
    public void ReplaceInlineFieldsRejectsDuplicateTarget()
    {
        FcbDocument source = ReadDocument([1]);
        FcbValueSchema schema = ReadSchema("00000010 00000020 Boolean\n");

        Assert.Throws<ArgumentException>(() => FcbValueMutator.ReplaceInlineFields(
            source,
            [
                new(source.Root.Fields[0], new byte[] { 0 }),
                new(source.Root.Fields[0], new byte[] { 1 }),
            ],
            schema));
    }

    private static FcbDocument ReadDocument(byte[] value)
    {
        using var body = new MemoryStream();
        body.WriteByte(0);
        WriteUInt32(body, 0x10);
        body.WriteByte(1);
        WriteUInt32(body, 0x20);
        body.WriteByte(checked((byte)value.Length));
        body.Write(value);
        return ReadDocumentBody(body.ToArray());
    }

    private static FcbDocument ReadDocumentWithFields(params (uint Hash, byte[] Value)[] fields)
    {
        using var body = new MemoryStream();
        body.WriteByte(0);
        WriteUInt32(body, 0x10);
        body.WriteByte(checked((byte)fields.Length));
        foreach ((uint hash, byte[] value) in fields)
        {
            WriteUInt32(body, hash);
            body.WriteByte(checked((byte)value.Length));
            body.Write(value);
        }

        return ReadDocumentBody(body.ToArray());
    }

    private static FcbDocument ReadDocumentBody(byte[] body)
    {
        byte[] data = new byte[FcbReader.HeaderSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, FcbReader.Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), FcbReader.Version);
        body.CopyTo(data, FcbReader.HeaderSize);
        using var input = new MemoryStream(data, false);
        return FcbReader.Read(input);
    }

    private static FcbValueSchema ReadSchema(string text)
    {
        using var input = new StringReader(text);
        return FcbValueSchema.Load(input);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> data = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        output.Write(data);
    }
}
