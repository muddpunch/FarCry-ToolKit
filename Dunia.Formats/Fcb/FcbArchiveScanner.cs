using System.Buffers.Binary;
using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Fcb;

public static class FcbArchiveScanner
{
    public const int DefaultMaxPayloadSize = 64 * 1024 * 1024;

    public static async Task<FcbArchiveScanResult> ScanAsync(
        Stream data,
        FatV10Index index,
        int maxMatches = 100,
        int maxPayloadSize = DefaultMaxPayloadSize,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(index);
        if (!data.CanRead || !data.CanSeek)
        {
            throw new ArgumentException("DAT stream must be readable and seekable.", nameof(data));
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMatches);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPayloadSize, FcbReader.HeaderSize);

        var matches = new List<FcbArchiveMatch>();
        int scanned = 0;
        int skipped = 0;
        for (int i = 0; i < index.Entries.Count && matches.Count < maxMatches; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FatV10Entry entry = index.Entries[i];
            if (entry.UncompressedSize < FcbReader.HeaderSize || entry.UncompressedSize > maxPayloadSize)
            {
                skipped++;
                continue;
            }

            scanned++;
            var prefix = new PrefixCaptureStream(sizeof(uint));
            await FatV10PayloadExtractor.ExtractAsync(data, entry, prefix, cancellationToken)
                .ConfigureAwait(false);
            if (prefix.Captured.Length == sizeof(uint) &&
                BinaryPrimitives.ReadUInt32LittleEndian(prefix.Captured.Span) == FcbReader.Signature)
            {
                matches.Add(new(i, entry.NameHash, entry.UncompressedSize, entry.CompressionScheme));
            }
        }

        return new(Array.AsReadOnly(matches.ToArray()), scanned, skipped);
    }

    private sealed class PrefixCaptureStream(int capacity) : Stream
    {
        private readonly byte[] data = new byte[capacity];
        private int captured;
        private long length;

        public ReadOnlyMemory<byte> Captured => data.AsMemory(0, captured);

        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => length;

        public override long Position
        {
            get => Length;
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public override void Write(byte[] buffer, int offset, int count)
        {
            Capture(buffer.AsSpan(offset, count));
        }

        public override void Write(ReadOnlySpan<byte> buffer) => Capture(buffer);

        public override ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Capture(buffer.Span);
            return ValueTask.CompletedTask;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        private void Capture(ReadOnlySpan<byte> buffer)
        {
            int count = Math.Min(buffer.Length, data.Length - captured);
            buffer[..count].CopyTo(data.AsSpan(captured));
            captured += count;
            length = checked(length + buffer.Length);
        }
    }
}
