using System.Buffers.Binary;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10EntryWriter
{
    public const int MaxUncompressedSize = 0x3FFFFFFF;
    public const int MaxStoredSize = 0x1FFFFFFF;
    public const long MaxOffset = 0x7FFFFFFFF;

    public static void Write(FatV10Entry entry, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (destination.Length != FatV10IndexSummaryReader.EntrySize)
        {
            throw new ArgumentException("FAT v10 entry destination must contain exactly 20 bytes.", nameof(destination));
        }

        Validate(entry);

        BinaryPrimitives.WriteUInt32LittleEndian(destination, (uint)(entry.NameHash >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], (uint)entry.NameHash);

        uint sizeAndFlags = ((uint)entry.UncompressedSize << 2)
            | ((uint)entry.CompressionScheme << 1)
            | (entry.IsEncrypted ? 1u : 0u);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], sizeAndFlags);

        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], (uint)(entry.Offset >> 3));
        uint offsetAndSize = ((uint)(entry.Offset & 7) << 29) | (uint)entry.StoredSize;
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], offsetAndSize);
    }

    internal static void Validate(FatV10Entry entry)
    {
        if ((uint)entry.UncompressedSize > MaxUncompressedSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entry),
                $"Uncompressed size must be between 0 and {MaxUncompressedSize}.");
        }

        if ((uint)entry.StoredSize > MaxStoredSize)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entry),
                $"Stored size must be between 0 and {MaxStoredSize}.");
        }

        if ((ulong)entry.Offset > MaxOffset)
        {
            throw new ArgumentOutOfRangeException(
                nameof(entry),
                $"Offset must be between 0 and {MaxOffset}.");
        }

        if (entry.CompressionScheme is not FatV10CompressionScheme.None and not FatV10CompressionScheme.Lz4)
        {
            throw new ArgumentOutOfRangeException(nameof(entry), "Unsupported FAT v10 compression scheme.");
        }

        if (entry.CompressionScheme == FatV10CompressionScheme.None
            && entry.StoredSize != entry.UncompressedSize)
        {
            throw new ArgumentException(
                "Uncompressed FAT v10 entries require matching stored and uncompressed sizes.",
                nameof(entry));
        }
    }
}
