using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10PayloadExtractorTests
{
    [Fact]
    public async Task ExtractAsyncCopiesOnlySelectedUncompressedPayload()
    {
        await using MemoryStream data = new([10, 20, 30, 40, 50, 60]);
        data.Position = 1;
        await using MemoryStream destination = new();
        FatV10Entry entry = CreateEntry(offset: 2, storedSize: 3, uncompressedSize: 3);

        await FatV10PayloadExtractor.ExtractAsync(
            data,
            entry,
            destination,
            TestContext.Current.CancellationToken);

        Assert.Equal([30, 40, 50], destination.ToArray());
        Assert.Equal(1, data.Position);
    }

    [Fact]
    public async Task ExtractAsyncRejectsPayloadOutsideDatBeforeWriting()
    {
        await using MemoryStream data = new([10, 20, 30]);
        await using MemoryStream destination = new();
        FatV10Entry entry = CreateEntry(offset: 2, storedSize: 2, uncompressedSize: 2);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => FatV10PayloadExtractor.ExtractAsync(
                data,
                entry,
                destination,
                TestContext.Current.CancellationToken));

        Assert.Empty(destination.ToArray());
    }

    [Fact]
    public async Task ExtractAsyncRejectsUncompressedSizeMismatch()
    {
        await using MemoryStream data = new([10, 20, 30]);
        await using MemoryStream destination = new();
        FatV10Entry entry = CreateEntry(offset: 0, storedSize: 2, uncompressedSize: 3);

        await Assert.ThrowsAsync<InvalidDataException>(
            () => FatV10PayloadExtractor.ExtractAsync(
                data,
                entry,
                destination,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExtractAsyncRejectsCompressedPayload()
    {
        await using MemoryStream data = new([10, 20, 30]);
        await using MemoryStream destination = new();
        FatV10Entry entry = CreateEntry(
            offset: 0,
            storedSize: 2,
            uncompressedSize: 3,
            compressionScheme: FatV10CompressionScheme.Lz4);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => FatV10PayloadExtractor.ExtractAsync(
                data,
                entry,
                destination,
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ExtractAsyncRejectsEncryptedPayload()
    {
        await using MemoryStream data = new([10, 20, 30]);
        await using MemoryStream destination = new();
        FatV10Entry entry = CreateEntry(offset: 0, storedSize: 2, uncompressedSize: 2, isEncrypted: true);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => FatV10PayloadExtractor.ExtractAsync(
                data,
                entry,
                destination,
                TestContext.Current.CancellationToken));
    }

    private static FatV10Entry CreateEntry(
        long offset,
        int storedSize,
        int uncompressedSize,
        FatV10CompressionScheme compressionScheme = FatV10CompressionScheme.None,
        bool isEncrypted = false) =>
        new(0x1234, uncompressedSize, offset, storedSize, compressionScheme, isEncrypted);
}
