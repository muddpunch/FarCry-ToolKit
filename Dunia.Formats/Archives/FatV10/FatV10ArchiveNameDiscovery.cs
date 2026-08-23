using System.Text;
using Dunia.Formats.Hashing;

namespace Dunia.Formats.Archives.FatV10;

public static class FatV10ArchiveNameDiscovery
{
    public const int DefaultMaxDecodedEntrySize = 64 * 1024 * 1024;

    public static async Task<FatV10ArchiveNameDiscoveryResult> DiscoverAsync(
        Stream data,
        IReadOnlyList<FatV10Entry> entries,
        IEnumerable<ulong> targetHashes,
        IProgress<FatV10ArchiveNameDiscoveryProgress>? progress = null,
        int maxDecodedEntrySize = DefaultMaxDecodedEntrySize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(targetHashes);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxDecodedEntrySize, 1);

        var targets = targetHashes.ToHashSet();
        var matches = new List<FatV10ArchiveNameDiscoveryMatch>();
        var uniqueMatches = new HashSet<(ulong Hash, string Name)>();
        long candidateCount = 0;
        int scanned = 0;
        int skipped = 0;

        for (int i = 0; i < entries.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FatV10Entry entry = entries[i];

            if (entry.IsEncrypted ||
                (entry.CompressionScheme == FatV10CompressionScheme.Lz4 && entry.UncompressedSize > maxDecodedEntrySize))
            {
                skipped++;
                ReportProgress(progress, i + 1, entries.Count, matches.Count);
                continue;
            }

            var scanner = new CandidateScanningStream((hash, name) =>
            {
                if (targets.Contains(hash) && uniqueMatches.Add((hash, name)))
                {
                    matches.Add(new(i, hash, name));
                }
            });

            try
            {
                await FatV10PayloadExtractor.ExtractAsync(data, entry, scanner, cancellationToken).ConfigureAwait(false);
                scanner.Complete();
                candidateCount += scanner.CandidateCount;
                scanned++;
            }
            catch (Exception ex) when (ex is InvalidDataException or NotSupportedException)
            {
                skipped++;
            }

            ReportProgress(progress, i + 1, entries.Count, matches.Count);
        }

        progress?.Report(new(entries.Count, entries.Count, matches.Count));
        return new(scanned, skipped, candidateCount, matches.AsReadOnly());
    }

    private static void ReportProgress(
        IProgress<FatV10ArchiveNameDiscoveryProgress>? progress,
        int processed,
        int total,
        int matched)
    {
        if (processed == total || processed % 512 == 0)
        {
            progress?.Report(new(processed, total, matched));
        }
    }

    private sealed class CandidateScanningStream(Action<ulong, string> onCandidate) : Stream
    {
        private const int MinLength = 4;
        private const int MaxLength = 2048;
        private static readonly char[] TokenSeparators = [' ', '\t', '"', '\'', '<', '>', '|', ',', ';', '(', ')', '[', ']', '{', '}'];

        private readonly byte[] _candidate = GC.AllocateUninitializedArray<byte>(MaxLength);
        private int _candidateLength;
        private bool _overflow;

        public long CandidateCount { get; private set; }

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Write(byte[] buffer, int offset, int count) => Scan(buffer.AsSpan(offset, count));

        public override void Write(ReadOnlySpan<byte> buffer) => Scan(buffer);

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Scan(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public void Complete() => FlushCandidate();

        private void Scan(ReadOnlySpan<byte> buffer)
        {
            foreach (byte value in buffer)
            {
                if (value is >= 0x20 and <= 0x7E)
                {
                    if (_candidateLength < MaxLength)
                    {
                        _candidate[_candidateLength++] = value;
                    }
                    else
                    {
                        _overflow = true;
                    }
                }
                else
                {
                    FlushCandidate();
                }
            }
        }

        private void FlushCandidate()
        {
            if (!_overflow && _candidateLength >= MinLength)
            {
                CandidateCount++;
                string value = Encoding.ASCII.GetString(_candidate, 0, _candidateLength);
                Match(value);

                foreach (string token in value.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (token.Length >= MinLength && token.Length != value.Length)
                    {
                        Match(token);
                    }
                }
            }

            _candidateLength = 0;
            _overflow = false;
        }

        private void Match(string value)
        {
            if (!value.Contains('.') && !value.Contains('\\') && !value.Contains('/'))
            {
                return;
            }

            string normalized = DuniaPathHash.Normalize(value);
            onCandidate(DuniaCrc64.Compute(normalized), normalized);
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();
    }
}
