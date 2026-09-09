using System.Buffers.Binary;

namespace Dunia.Formats.Hashing;

public static class DuniaResourceReferenceScanner
{
    private const int BufferSize = 1024 * 1024;
    private const int HashSize = sizeof(ulong);
    private const int MaximumMatches = 1_000_000;

    public static async Task<IReadOnlyList<DuniaResourceReferenceMatch>> ScanAsync(
        Stream input,
        IReadOnlySet<ulong> candidateHashes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(candidateHashes);
        if (!input.CanRead)
        {
            throw new ArgumentException("Reference input stream must be readable.", nameof(input));
        }

        if (candidateHashes.Count == 0)
        {
            return Array.Empty<DuniaResourceReferenceMatch>();
        }

        byte[] buffer = GC.AllocateUninitializedArray<byte>(BufferSize + HashSize - 1);
        var matches = new List<DuniaResourceReferenceMatch>();
        int retained = 0;
        long bufferOffset = input.CanSeek ? input.Position : 0;

        while (true)
        {
            int read = await input.ReadAsync(
                buffer.AsMemory(retained, BufferSize - retained), cancellationToken).ConfigureAwait(false);
            int available = retained + read;
            int windowCount = Math.Max(0, available - HashSize + 1);

            for (int i = 0; i < windowCount; i++)
            {
                ReadOnlySpan<byte> bytes = buffer.AsSpan(i, HashSize);
                ulong littleEndian = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
                if (candidateHashes.Contains(littleEndian))
                {
                    Add(matches, new(bufferOffset + i, littleEndian, DuniaResourceReferenceEndianness.LittleEndian));
                }

                ulong bigEndian = BinaryPrimitives.ReadUInt64BigEndian(bytes);
                if (bigEndian != littleEndian && candidateHashes.Contains(bigEndian))
                {
                    Add(matches, new(bufferOffset + i, bigEndian, DuniaResourceReferenceEndianness.BigEndian));
                }
            }

            if (read == 0)
            {
                break;
            }

            retained = Math.Min(HashSize - 1, available);
            bufferOffset += available - retained;
            buffer.AsSpan(available - retained, retained).CopyTo(buffer);
        }

        return matches.AsReadOnly();
    }

    private static void Add(List<DuniaResourceReferenceMatch> matches, DuniaResourceReferenceMatch match)
    {
        if (matches.Count == MaximumMatches)
        {
            throw new InvalidDataException("Reference match count exceeds the 1,000,000 safety limit.");
        }

        matches.Add(match);
    }
}
