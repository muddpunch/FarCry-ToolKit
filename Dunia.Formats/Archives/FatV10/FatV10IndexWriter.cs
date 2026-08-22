using System.Buffers.Binary;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10IndexWriter
{
    public static void Write(Stream output, IReadOnlyList<FatV10Entry> entries)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(entries);

        if (!output.CanWrite)
        {
            throw new ArgumentException("FAT index output must be writable.", nameof(output));
        }

        // Validate the complete index before publishing any bytes.
        for (int i = 0; i < entries.Count; i++)
        {
            FatV10Entry? entry = entries[i];
            if (entry is null)
            {
                throw new ArgumentException($"FAT v10 entry {i} cannot be null.", nameof(entries));
            }

            FatV10EntryWriter.Validate(entry);
        }

        Span<byte> buffer = stackalloc byte[FatV10IndexSummaryReader.HeaderSize];
        buffer.Clear();
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[4..], FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[8..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(buffer[20..], entries.Count);
        output.Write(buffer);

        Span<byte> entryBuffer = stackalloc byte[FatV10IndexSummaryReader.EntrySize];
        foreach (FatV10Entry entry in entries)
        {
            FatV10EntryWriter.Write(entry, entryBuffer);
            output.Write(entryBuffer);
        }

        Span<byte> trailer = stackalloc byte[FatV10IndexSummaryReader.TrailerSize];
        trailer.Clear();
        output.Write(trailer);
    }
}
