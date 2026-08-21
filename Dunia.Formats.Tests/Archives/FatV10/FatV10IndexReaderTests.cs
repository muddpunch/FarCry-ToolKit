using System.Buffers.Binary;
using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10IndexReaderTests
{
    [Fact]
    public void ReadReturnsEntriesAndRestoresPosition()
    {
        using MemoryStream input = CreateSingleEntryIndex(offset: 8, storedSize: 4);
        input.Position = 3;

        FatV10Index index = FatV10IndexReader.Read(input, 12);

        FatV10Entry entry = Assert.Single(index.Entries);
        Assert.Equal(8, entry.Offset);
        Assert.Equal(4, entry.StoredSize);
        Assert.Equal(3, input.Position);
    }

    [Fact]
    public void ReadRejectsEntryOutsidePairedData()
    {
        using MemoryStream input = CreateSingleEntryIndex(offset: 8, storedSize: 5);

        Assert.Throws<InvalidDataException>(() => FatV10IndexReader.Read(input, 12));
    }

    private static MemoryStream CreateSingleEntryIndex(uint offset, int storedSize)
    {
        byte[] bytes = new byte[52];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), offset >> 3);
        BinaryPrimitives.WriteUInt32LittleEndian(
            bytes.AsSpan(40),
            ((offset & 7) << 29) | (uint)storedSize);
        return new(bytes, true);
    }
}

