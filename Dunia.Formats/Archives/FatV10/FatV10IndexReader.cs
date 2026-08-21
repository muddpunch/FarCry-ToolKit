namespace Dunia.Formats.Archives.FatV10;

public static class FatV10IndexReader
{
    public static FatV10Index Read(Stream input, long? dataLength = null)
    {
        ArgumentNullException.ThrowIfNull(input);

        if (dataLength < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dataLength));
        }

        FatV10IndexSummary summary = FatV10IndexSummaryReader.Read(input);
        var entries = new FatV10Entry[summary.EntryCount];
        Span<byte> data = stackalloc byte[FatV10IndexSummaryReader.EntrySize];
        long originalPosition = input.Position;

        try
        {
            input.Position = FatV10IndexSummaryReader.HeaderSize;

            for (int index = 0; index < entries.Length; index++)
            {
                input.ReadExactly(data);
                FatV10Entry entry = FatV10EntryReader.Read(data);

                if (dataLength.HasValue &&
                    (entry.Offset > dataLength.Value || entry.StoredSize > dataLength.Value - entry.Offset))
                {
                    throw new InvalidDataException(
                        $"FAT v10 entry {index} exceeds the paired DAT length.");
                }

                entries[index] = entry;
            }

            return new(summary, Array.AsReadOnly(entries));
        }
        finally
        {
            input.Position = originalPosition;
        }
    }
}

