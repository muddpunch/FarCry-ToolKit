using System.Buffers;

namespace Dunia.Formats.Textures;

public static class XbtDdsExtractor
{
    private const int BufferSize = 80 * 1024;
    private const int MaximumHeaderLength = 1024 * 1024;
    private const uint DdsMagic = 0x44445320;
    private static readonly byte[] DdsMagicBytes = "DDS "u8.ToArray();

    public static async Task<XbtDdsExtractionResult> ExtractAsync(
        Stream xbt,
        Stream dds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(xbt);
        ArgumentNullException.ThrowIfNull(dds);

        if (!xbt.CanRead)
        {
            throw new ArgumentException("XBT stream must be readable.", nameof(xbt));
        }

        if (!dds.CanWrite)
        {
            throw new ArgumentException("DDS stream must be writable.", nameof(dds));
        }

        if (ReferenceEquals(xbt, dds))
        {
            throw new ArgumentException("Input and output streams must be different.", nameof(dds));
        }

        byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

        try
        {
            long scanned = 0;
            uint window = 0;
            int windowLength = 0;

            while (true)
            {
                int read = await xbt.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    throw new InvalidDataException("XBT does not contain a DDS payload.");
                }

                for (int index = 0; index < read; index++)
                {
                    window = (window << 8) | buffer[index];
                    windowLength = Math.Min(windowLength + 1, sizeof(uint));

                    if (windowLength < sizeof(uint) || window != DdsMagic)
                    {
                        continue;
                    }

                    long headerLength = scanned + index - (sizeof(uint) - 1);
                    if (headerLength > MaximumHeaderLength)
                    {
                        throw new InvalidDataException("XBT header exceeds the supported probe limit.");
                    }

                    return await WritePayloadAsync(
                        xbt,
                        dds,
                        buffer,
                        index + 1,
                        read,
                        headerLength,
                        cancellationToken).ConfigureAwait(false);
                }

                scanned += read;
                if (scanned > MaximumHeaderLength + sizeof(uint))
                {
                    throw new InvalidDataException("XBT does not contain a DDS payload near its header.");
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, true);
        }
    }

    private static async Task<XbtDdsExtractionResult> WritePayloadAsync(
        Stream xbt,
        Stream dds,
        byte[] buffer,
        int payloadOffset,
        int bufferedLength,
        long headerLength,
        CancellationToken cancellationToken)
    {
        await dds.WriteAsync(DdsMagicBytes, cancellationToken).ConfigureAwait(false);
        long ddsLength = DdsMagicBytes.Length;
        int remaining = bufferedLength - payloadOffset;

        if (remaining > 0)
        {
            await dds.WriteAsync(
                buffer.AsMemory(payloadOffset, remaining),
                cancellationToken).ConfigureAwait(false);
            ddsLength += remaining;
        }

        while (true)
        {
            int read = await xbt.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return new(headerLength, ddsLength);
            }

            await dds.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
            ddsLength = checked(ddsLength + read);
        }
    }
}

