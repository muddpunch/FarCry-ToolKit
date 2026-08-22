using System.Buffers.Binary;
using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbWriterTests
{
    [Fact]
    public void WriteRoundTripsInlineFieldsAndHeaderCountersByteExactly()
    {
        using var body = new MemoryStream();
        body.WriteByte(0);
        WriteUInt32(body, 0x11223344);
        body.WriteByte(1);
        WriteUInt32(body, 0x55667788);
        body.WriteByte(3);
        body.Write([1, 2, 3]);
        byte[] source = Wrap(body.ToArray(), 7, 11);

        AssertRoundTrip(source);
    }

    [Fact]
    public void WriteRoundTripsValueReferencesAndSharedNodesByteExactly()
    {
        using var body = new MemoryStream();
        body.WriteByte(2);
        WriteUInt32(body, 1);
        body.WriteByte(2);
        WriteUInt32(body, 10);
        long inlineOffset = body.Position + FcbReader.HeaderSize;
        body.WriteByte(3);
        body.Write([4, 5, 6]);
        WriteUInt32(body, 11);
        long referenceOffset = body.Position + FcbReader.HeaderSize;
        body.WriteByte(0xFE);
        WriteUInt32(body, checked((uint)(referenceOffset - inlineOffset)));
        body.WriteByte(0);
        WriteUInt32(body, 2);
        body.WriteByte(0);
        body.WriteByte(0xFE);
        WriteUInt32(body, 1);
        byte[] source = Wrap(body.ToArray(), 2, 2);

        AssertRoundTrip(source);
    }

    [Fact]
    public void WriteRejectsNonEmptyOutput()
    {
        byte[] source = Wrap([0, 1, 0, 0, 0, 0], 0, 0);
        using var input = new MemoryStream(source, false);
        FcbDocument document = FcbReader.Read(input);
        using var output = new MemoryStream([0]);

        Assert.Throws<ArgumentException>(() => FcbWriter.Write(output, document));
    }

    private static void AssertRoundTrip(byte[] source)
    {
        using var input = new MemoryStream(source, false);
        FcbDocument document = FcbReader.Read(input);
        using var output = new MemoryStream();

        FcbWriter.Write(output, document);

        Assert.Equal(source, output.ToArray());
    }

    private static byte[] Wrap(byte[] body, uint objects, uint values)
    {
        byte[] data = new byte[FcbReader.HeaderSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, FcbReader.Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), FcbReader.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), objects);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), values);
        body.CopyTo(data, FcbReader.HeaderSize);
        return data;
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> data = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        output.Write(data);
    }
}
