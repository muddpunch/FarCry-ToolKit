using System.Buffers.Binary;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10IndexSummaryReader
{
    public const uint Signature = 0x46415432;
    public const int Version = 10;
    public const int HeaderSize = 24;
    public const int EntrySize = 20;
    public const int TrailerSize = 8;

    public static FatV10IndexSummary Read(Stream input)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (!input.CanRead || !input.CanSeek)
        {
            throw new ArgumentException("FAT summary requires a readable, seekable stream.", nameof(input));
        }

        long originalPosition = input.Position;

        try
        {
            Span<byte> header = stackalloc byte[HeaderSize];
            input.Position = 0;
            input.ReadExactly(header);

            uint signature = BinaryPrimitives.ReadUInt32LittleEndian(header);
            int version = BinaryPrimitives.ReadInt32LittleEndian(header[4..]);
            int platform = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
            int subFatEntryCount = BinaryPrimitives.ReadInt32LittleEndian(header[12..]);
            int subFatCount = BinaryPrimitives.ReadInt32LittleEndian(header[16..]);
            int entryCount = BinaryPrimitives.ReadInt32LittleEndian(header[20..]);

            if (signature != Signature)
            {
                throw new InvalidDataException("Invalid FAT2 signature.");
            }

            if (version != Version)
            {
                throw new InvalidDataException($"Expected FAT v10, got v{version}.");
            }

            if (platform != 1)
            {
                throw new InvalidDataException($"Unsupported FAT v10 platform value {platform}.");
            }

            if (subFatEntryCount != 0 || subFatCount != 0)
            {
                throw new NotSupportedException("FAT v10 SubFAT sections are not yet supported.");
            }

            if (entryCount < 0)
            {
                throw new InvalidDataException("FAT v10 entry count cannot be negative.");
            }

            long expectedLength = checked(HeaderSize + ((long)entryCount * EntrySize) + TrailerSize);
            if (input.Length != expectedLength)
            {
                throw new InvalidDataException(
                    $"FAT v10 length mismatch: expected {expectedLength}, got {input.Length}.");
            }

            Span<byte> trailer = stackalloc byte[TrailerSize];
            input.Position = expectedLength - TrailerSize;
            input.ReadExactly(trailer);

            if (BinaryPrimitives.ReadUInt64LittleEndian(trailer) != 0)
            {
                throw new InvalidDataException("FAT v10 trailer contains unsupported sections.");
            }

            return new(platform, entryCount, EntrySize, expectedLength);
        }
        finally
        {
            input.Position = originalPosition;
        }
    }
}

