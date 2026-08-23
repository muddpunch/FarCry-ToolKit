using System.Text;

namespace Dunia.Formats.Hashing;

public static class DuniaPathNameDiscovery
{
    private const int BufferSize = 256 * 1024;
    private const int MinLength = 4;
    private const int MaxLength = 2048;

    public static async Task<DuniaPathNameDiscoveryResult> DiscoverAsync(
        Stream input,
        IEnumerable<ulong> targetHashes,
        IProgress<DuniaPathNameDiscoveryProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(targetHashes);
        if (!input.CanRead)
        {
            throw new ArgumentException("Input must be readable.", nameof(input));
        }

        var targets = targetHashes.ToHashSet();
        var uniqueMatches = new HashSet<(ulong Hash, string Name)>();
        var matches = new List<DuniaPathNameDiscoveryMatch>();
        long candidateCount = 0;

        void Match(string value, long sourceOffset)
        {
            if (!LooksLikePath(value))
            {
                return;
            }

            candidateCount++;
            string normalized = DuniaPathHash.Normalize(value);
            ulong hash = DuniaCrc64.Compute(normalized);
            if (targets.Contains(hash) && uniqueMatches.Add((hash, normalized)))
            {
                matches.Add(new(hash, normalized, sourceOffset));
            }
        }

        var ascii = new CandidateScanner(Match);
        var wideEven = new WideCandidateScanner(Match);
        var wideOdd = new WideCandidateScanner(Match);
        byte[] buffer = GC.AllocateUninitializedArray<byte>(BufferSize);
        long startOffset = input.CanSeek ? input.Position : 0;
        long sourceOffset = startOffset;
        long totalBytes = input.CanSeek ? input.Length - startOffset : -1;
        long nextProgress = BufferSize;

        int read;
        while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
        {
            for (int i = 0; i < read; i++, sourceOffset++)
            {
                byte value = buffer[i];
                ascii.Scan(value, sourceOffset);
                wideEven.Scan(value, sourceOffset, 0);
                wideOdd.Scan(value, sourceOffset, 1);
            }

            long processed = sourceOffset - startOffset;
            if (processed >= nextProgress)
            {
                progress?.Report(new(processed, totalBytes, matches.Count));
                nextProgress = processed + BufferSize;
            }
        }

        ascii.Complete();
        wideEven.Complete();
        wideOdd.Complete();
        progress?.Report(new(sourceOffset - startOffset, totalBytes, matches.Count));
        return new(candidateCount, matches.AsReadOnly());
    }

    private static bool LooksLikePath(string value) =>
        value.Contains('.') || value.Contains('\\') || value.Contains('/');

    private sealed class CandidateScanner(Action<string, long> onCandidate)
    {
        private static readonly char[] TokenSeparators =
            [' ', '\t', '"', '\'', '<', '>', '|', ',', ';', '(', ')', '[', ']', '{', '}', '='];

        private readonly byte[] _candidate = GC.AllocateUninitializedArray<byte>(MaxLength);
        private int _length;
        private bool _overflow;
        private long _offset;

        public void Scan(byte value, long sourceOffset)
        {
            if (value is >= 0x20 and <= 0x7E)
            {
                if (_length == 0 && !_overflow)
                {
                    _offset = sourceOffset;
                }

                if (_length < MaxLength)
                {
                    _candidate[_length++] = value;
                }
                else
                {
                    _overflow = true;
                }

                return;
            }

            Flush();
        }

        public void Complete() => Flush();

        private void Flush()
        {
            if (!_overflow && _length >= MinLength)
            {
                string value = Encoding.ASCII.GetString(_candidate, 0, _length);
                Emit(value);
            }

            _length = 0;
            _overflow = false;
        }

        private void Emit(string value)
        {
            onCandidate(value, _offset);
            foreach (string token in value.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length >= MinLength && token.Length != value.Length)
                {
                    onCandidate(token, _offset);
                }
            }
        }
    }

    private sealed class WideCandidateScanner(Action<string, long> onCandidate)
    {
        private readonly char[] _candidate = GC.AllocateUninitializedArray<char>(MaxLength);
        private int _length;
        private bool _overflow;
        private bool _hasLowByte;
        private byte _lowByte;
        private long _lowByteOffset;
        private long _candidateOffset;

        public void Scan(byte value, long sourceOffset, int lowByteParity)
        {
            if ((sourceOffset & 1) == lowByteParity)
            {
                _lowByte = value;
                _lowByteOffset = sourceOffset;
                _hasLowByte = true;
                return;
            }

            if (!_hasLowByte)
            {
                return;
            }

            if (_lowByte is >= 0x20 and <= 0x7E && value == 0)
            {
                if (_length == 0 && !_overflow)
                {
                    _candidateOffset = _lowByteOffset;
                }

                if (_length < MaxLength)
                {
                    _candidate[_length++] = (char)_lowByte;
                }
                else
                {
                    _overflow = true;
                }
            }
            else
            {
                Flush();
            }

            _hasLowByte = false;
        }

        public void Complete() => Flush();

        private void Flush()
        {
            if (!_overflow && _length >= MinLength)
            {
                string value = new(_candidate, 0, _length);
                onCandidate(value, _candidateOffset);
            }

            _length = 0;
            _overflow = false;
            _hasLowByte = false;
        }
    }
}

public sealed record DuniaPathNameDiscoveryMatch(ulong Hash, string Name, long SourceOffset);

public sealed record DuniaPathNameDiscoveryProgress(long ProcessedBytes, long TotalBytes, int MatchCount);

public sealed record DuniaPathNameDiscoveryResult(
    long CandidateCount,
    IReadOnlyList<DuniaPathNameDiscoveryMatch> Matches);
