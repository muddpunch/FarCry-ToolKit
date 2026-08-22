using System.Buffers.Binary;
using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbValueSchemaCoverageAnalyzerTests
{
    [Fact]
    public void AnalyzeSeparatesResolvedMissingAndIncompatibleFields()
    {
        using var body = new MemoryStream();
        body.WriteByte(0);
        WriteUInt32(body, 0x10);
        body.WriteByte(3);
        WriteField(body, 0x20, [1]);
        WriteField(body, 0x21, [2]);
        WriteField(body, 0x22, [3]);
        byte[] data = new byte[FcbReader.HeaderSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, FcbReader.Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), FcbReader.Version);
        body.ToArray().CopyTo(data, FcbReader.HeaderSize);
        using var documentInput = new MemoryStream(data, false);
        FcbDocument document = FcbReader.Read(documentInput);
        using var schemaInput = new StringReader("00000010 00000020 Boolean\n00000010 00000021 Boolean\n");
        FcbValueSchema schema = FcbValueSchema.Load(schemaInput);

        FcbValueSchemaCoverageReport report = FcbValueSchemaCoverageAnalyzer.Analyze(document, schema);

        Assert.Equal(3, report.FieldCount);
        Assert.Equal(1, report.ResolvedCount);
        Assert.Equal(1, report.MissingCount);
        Assert.Equal(1, report.IncompatibleCount);
        Assert.False(report.IsComplete);
    }

    private static void WriteField(Stream output, uint hash, byte[] data)
    {
        WriteUInt32(output, hash);
        output.WriteByte(checked((byte)data.Length));
        output.Write(data);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> data = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        output.Write(data);
    }
}
