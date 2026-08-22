using K4os.Compression.LZ4;

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

        if (entry.Offset < 0 || entry.StoredSize < 0 || entry.UncompressedSize < 0)
        {
            throw new InvalidDataException("FAT v10 entry contains a negative payload range.");
        }

        if (entry.CompressionScheme == FatV10CompressionScheme.None
            && entry.StoredSize != entry.UncompressedSize)
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
            switch (entry.CompressionScheme)
            {
                case FatV10CompressionScheme.None:
                    await CopyExactlyAsync(data, destination, entry.StoredSize, cancellationToken).ConfigureAwait(false);
                    break;
                case FatV10CompressionScheme.Lz4:
                    await ExtractLz4Async(data, destination, entry, cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    throw new NotSupportedException($"Unsupported compression scheme: {entry.CompressionScheme}.");
            }
        }
        finally
        {
            data.Position = originalPosition;
        }
    }

    private static async Task ExtractLz4Async(
        Stream source,
        Stream destination,
        FatV10Entry entry,
        CancellationToken cancellationToken)
    {
        if (entry.StoredSize == 0 || entry.UncompressedSize == 0)
        {
            if (entry.StoredSize == entry.UncompressedSize)
            {
                return;
            }

            throw new InvalidDataException("LZ4 FAT v10 entry has an invalid zero-sized payload.");
        }

        byte[] compressed = GC.AllocateUninitializedArray<byte>(entry.StoredSize);
        await ReadExactlyAsync(source, compressed, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        byte[] uncompressed = GC.AllocateUninitializedArray<byte>(entry.UncompressedSize);
        int decoded = LZ4Codec.Decode(compressed, uncompressed);
        if (decoded != uncompressed.Length)
        {
            throw new InvalidDataException(
                $"LZ4 payload decoded to {decoded} bytes; expected {uncompressed.Length}.");
        }

        await destination.WriteAsync(uncompressed, cancellationToken).ConfigureAwait(false);
    }

    private static async Task ReadExactlyAsync(
        Stream source,
        Memory<byte> destination,
        CancellationToken cancellationToken)
    {
        int position = 0;
        while (position < destination.Length)
        {
            int read = await source.ReadAsync(destination[position..], cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new EndOfStreamException("DAT stream ended before the complete payload was read.");
            }

            position += read;
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
