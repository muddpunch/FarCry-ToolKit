namespace Dunia.Formats.Archives.FatV10;

public static class FatV10PayloadExtractor
{
    public static async Task ExtractAsync(
        Stream data,
        FatV10Entry entry,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(destination);

        if (!data.CanRead || !data.CanSeek)
        {
            throw new ArgumentException("DAT stream must be readable and seekable.", nameof(data));
        }

        if (!destination.CanWrite)
        {
            throw new ArgumentException("Destination stream must be writable.", nameof(destination));
        }

        if (entry.IsEncrypted)
        {
            throw new NotSupportedException("Encrypted FAT v10 payloads are not supported.");
        }

        if (entry.CompressionScheme != FatV10CompressionScheme.None)
        {
            throw new NotSupportedException($"Compression scheme '{entry.CompressionScheme}' is not supported yet.");
        }

        if (entry.Offset < 0 || entry.StoredSize < 0 || entry.UncompressedSize < 0)
        {
            throw new InvalidDataException("FAT v10 entry contains a negative payload range.");
        }

        if (entry.StoredSize != entry.UncompressedSize)
        {
            throw new InvalidDataException("Uncompressed FAT v10 entry has mismatched stored and output sizes.");
        }

        if (entry.Offset > data.Length || entry.StoredSize > data.Length - entry.Offset)
        {
            throw new InvalidDataException("FAT v10 payload range exceeds the DAT stream.");
        }

        long originalPosition = data.Position;
        try
        {
            data.Position = entry.Offset;
            await CopyExactlyAsync(data, destination, entry.StoredSize, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            data.Position = originalPosition;
        }
    }

    private static async Task CopyExactlyAsync(
        Stream source,
        Stream destination,
        int length,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[Math.Min(length, 80 * 1024)];
        int remaining = length;

        while (remaining > 0)
        {
            int read = await source.ReadAsync(
                buffer.AsMemory(0, Math.Min(buffer.Length, remaining)),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("DAT stream ended before the complete payload was read.");
            }

            await destination.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            remaining -= read;
        }
    }
}
