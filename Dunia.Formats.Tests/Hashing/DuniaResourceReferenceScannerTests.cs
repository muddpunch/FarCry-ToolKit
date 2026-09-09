using System.Buffers.Binary;
using Dunia.Formats.Hashing;

namespace Dunia.Formats.Tests.Hashing;

public sealed class DuniaResourceReferenceScannerTests
{
    [Fact]
    public async Task ScanFindsLittleAndBigEndianHashesAcrossBufferBoundary()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        const ulong little = 0x0123456789ABCDEF;
        const ulong big = 0x1020304050607080;
        byte[] payload = new byte[1024 * 1024 + 32];
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(1024 * 1024 - 3), little);
        BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(1024 * 1024 + 11), big);

        IReadOnlyList<DuniaResourceReferenceMatch> matches = await DuniaResourceReferenceScanner.ScanAsync(
            new MemoryStream(payload), new HashSet<ulong> { little, big }, token);

        Assert.Equal(
            [
                new(1024 * 1024 - 3, little, DuniaResourceReferenceEndianness.LittleEndian),
                new(1024 * 1024 + 11, big, DuniaResourceReferenceEndianness.BigEndian),
            ],
            matches);
    }

    [Fact]
    public async Task ScanUsesCurrentStreamPositionAsAbsoluteOffset()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        const ulong hash = 0x0123456789ABCDEF;
        byte[] payload = new byte[32];
        BinaryPrimitives.WriteUInt64LittleEndian(payload.AsSpan(12), hash);
        using var input = new MemoryStream(payload) { Position = 5 };

        IReadOnlyList<DuniaResourceReferenceMatch> matches = await DuniaResourceReferenceScanner.ScanAsync(
            input, new HashSet<ulong> { hash }, token);

        Assert.Equal(12, Assert.Single(matches).Offset);
    }

    [Fact]
    public async Task ScanReturnsEmptyForNoCandidatesWithoutReadingInput()
    {
        using var input = new ThrowingReadStream();

        IReadOnlyList<DuniaResourceReferenceMatch> matches = await DuniaResourceReferenceScanner.ScanAsync(
            input, new HashSet<ulong>(), TestContext.Current.CancellationToken);

        Assert.Empty(matches);
    }

    private sealed class ThrowingReadStream : MemoryStream
    {
        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    }
}
