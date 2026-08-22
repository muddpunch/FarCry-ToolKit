using System.Buffers.Binary;
using Dunia.Formats.Fcb;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbReaderTests
{
    [Fact]
    public void ReadParsesInlineFieldAndRestoresPosition()
    {
        using MemoryStream input = CreateSimpleDocument();
        input.Position = 3;

        FcbDocument document = FcbReader.Read(input);

        Assert.Equal(FcbReader.Version, document.Header.Version);
        Assert.Equal(0x11223344U, document.Root.TypeHash);
        FcbField field = Assert.Single(document.Root.Fields);
        Assert.Equal(0x55667788U, field.NameHash);
        Assert.Equal(new byte[] { 1, 2, 3 }, field.Data.ToArray());
        Assert.False(field.IsReference);
        Assert.Equal(3, input.Position);
    }

    [Fact]
    public void ReadResolvesNodeAndValueReferences()
    {
        using var body = new MemoryStream();
        body.WriteByte(2); // root children
        WriteUInt32(body, 1);
        body.WriteByte(2); // root fields
        WriteUInt32(body, 10);
        long inlineOffset = body.Position;
        body.WriteByte(3);
        body.Write([4, 5, 6]);
        WriteUInt32(body, 11);
        long referenceOffset = body.Position;
        body.WriteByte(0xFE);
        WriteUInt32(body, checked((uint)(referenceOffset - inlineOffset)));
        body.WriteByte(0); // child body children
        WriteUInt32(body, 2);
        body.WriteByte(0); // child body fields
        body.WriteByte(0xFE); // second child points to pointer index 1
        WriteUInt32(body, 1);
        using MemoryStream input = Wrap(body.ToArray(), objectCount: 2, valueCount: 2);

        FcbDocument document = FcbReader.Read(input);

        Assert.Equal(2, document.UniqueNodeCount);
        Assert.Equal(2, document.FieldCount);
        Assert.Same(document.Root.Children[0], document.Root.Children[1]);
        Assert.True(document.Root.Fields[1].IsReference);
        Assert.Same(document.Root.Fields[0], document.Root.Fields[1].ReferenceTarget);
        Assert.Equal(new byte[] { 4, 5, 6 }, document.Root.Fields[1].Data.ToArray());
    }

    [Fact]
    public void ReadRejectsUnknownValueReference()
    {
        using var body = new MemoryStream();
        body.WriteByte(0);
        WriteUInt32(body, 1);
        body.WriteByte(1);
        WriteUInt32(body, 2);
        body.WriteByte(0xFE);
        WriteUInt32(body, 1);
        using MemoryStream input = Wrap(body.ToArray(), 0, 1);

        Assert.Throws<InvalidDataException>(() => FcbReader.Read(input));
    }

    private static MemoryStream CreateSimpleDocument()
    {
        using var body = new MemoryStream();
        body.WriteByte(0);
        WriteUInt32(body, 0x11223344);
        body.WriteByte(1);
        WriteUInt32(body, 0x55667788);
        body.WriteByte(3);
        body.Write([1, 2, 3]);
        return Wrap(body.ToArray(), 0, 1);
    }

    private static MemoryStream Wrap(byte[] body, uint objectCount, uint valueCount)
    {
        byte[] data = new byte[FcbReader.HeaderSize + body.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(data, FcbReader.Signature);
        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(4), FcbReader.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(8), objectCount);
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(12), valueCount);
        body.CopyTo(data, FcbReader.HeaderSize);
        return new(data, false);
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> data = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32LittleEndian(data, value);
        output.Write(data);
    }
}
