using System.Text;
using Dunia.Formats.Hashing;

namespace Dunia.Formats.Fcb;

public static class FcbNameDiscovery
{
    public const int DefaultMinLength = 2;
    public const int DefaultMaxLength = 1024;

    public static FcbNameDiscoveryResult ScanAscii(
        Stream input,
        IEnumerable<uint> targetHashes,
        int minLength = DefaultMinLength,
        int maxLength = DefaultMaxLength)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(targetHashes);
        if (!input.CanRead)
        {
            throw new ArgumentException("Candidate input must be readable.", nameof(input));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(minLength, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLength, minLength);

        var targets = targetHashes.ToHashSet();
        var matchedHashes = new HashSet<uint>();
        var matchedNames = new HashSet<(uint Hash, string Name)>();
        var matches = new List<FcbNameDiscoveryMatch>();
        byte[] candidate = GC.AllocateUninitializedArray<byte>(maxLength);
        byte[] buffer = GC.AllocateUninitializedArray<byte>(64 * 1024);
        int candidateLength = 0;
        bool overflow = false;
        long sourceOffset = input.CanSeek ? input.Position : 0;
        long candidateOffset = sourceOffset;
        long candidateCount = 0;

        void Flush()
        {
            if (!overflow && candidateLength >= minLength)
            {
                candidateCount++;
                uint hash = DuniaCrc32.Compute(candidate.AsSpan(0, candidateLength));
                if (targets.Contains(hash))
                {
                    string name = Encoding.ASCII.GetString(candidate, 0, candidateLength);
                    if (matchedNames.Add((hash, name)))
                    {
                        matches.Add(new(hash, name, candidateOffset));
                        matchedHashes.Add(hash);
                    }
                }
            }

            candidateLength = 0;
            overflow = false;
        }

        int read;
        while ((read = input.Read(buffer)) > 0)
        {
            for (int i = 0; i < read; i++, sourceOffset++)
            {
                byte value = buffer[i];
                if (value is >= 0x20 and <= 0x7E)
                {
                    if (candidateLength == 0 && !overflow)
                    {
                        candidateOffset = sourceOffset;
                    }

                    if (candidateLength < maxLength)
                    {
                        candidate[candidateLength++] = value;
                    }
                    else
                    {
                        overflow = true;
                    }

                    continue;
                }

                Flush();
            }
        }

        Flush();
        return new(
            targets.Count,
            candidateCount,
            matches.AsReadOnly(),
            Array.AsReadOnly(targets.Except(matchedHashes).Order().ToArray()));
    }
}
