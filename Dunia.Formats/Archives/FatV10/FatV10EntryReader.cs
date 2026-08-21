using System.Buffers.Binary;

namespace Dunia.Formats.Archives.FatV10;

// Layout adapted from Gibbed.Dunia's zlib-licensed EntrySerializerV10.
public static class FatV10EntryReader
{
    public static FatV10Entry Read(ReadOnlySpan<byte> data)
    {
        if (data.Length != FatV10IndexSummaryReader.EntrySize)
        {
            throw new ArgumentException("FAT v10 entry must contain exactly 20 bytes.", nameof(data));
        }

        uint hashUpper = BinaryPrimitives.ReadUInt32LittleEndian(data);
        uint hashLower = BinaryPrimitives.ReadUInt32LittleEndian(data[4..]);
        uint sizeAndFlags = BinaryPrimitives.ReadUInt32LittleEndian(data[8..]);
        uint offsetUpper = BinaryPrimitives.ReadUInt32LittleEndian(data[12..]);
        uint offsetAndSize = BinaryPrimitives.ReadUInt32LittleEndian(data[16..]);

        return new(
            ((ulong)hashUpper << 32) | hashLower,
            (int)(sizeAndFlags >> 2),
            (long)(((ulong)offsetUpper << 3) | (offsetAndSize >> 29)),
            (int)(offsetAndSize & 0x1FFFFFFF),
            (FatV10CompressionScheme)((sizeAndFlags >> 1) & 1),
            (sizeAndFlags & 1) != 0);
    }
}

