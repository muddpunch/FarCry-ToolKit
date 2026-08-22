using System.Buffers.Binary;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10ArchivePatchBuilderTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit.Tests",
        Guid.NewGuid().ToString("N"));

    public FatV10ArchivePatchBuilderTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task BuildWithoutReplacementsIsByteExact()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] datBytes = [1, 2, 3, 4];
        byte[] fatBytes = CreateIndex(new(1, 4, 0, 4, FatV10CompressionScheme.None, false));
        using var fatInput = new MemoryStream(fatBytes, false);
        using var datInput = new MemoryStream(datBytes, false);
        using var fatOutput = new MemoryStream();
        using var datOutput = new MemoryStream();
        using var store = new ReplacementStagingStore(directory);

        FatV10ArchivePatchBuildResult result = await FatV10ArchivePatchBuilder.BuildAsync(
            fatInput,
            datInput,
            fatOutput,
            datOutput,
            new Dictionary<int, StagedReplacement>(),
            store,
            token);

        Assert.Equal(fatBytes, fatOutput.ToArray());
        Assert.Equal(datBytes, datOutput.ToArray());
        Assert.Equal(0, result.ReplacementCount);
    }

    [Fact]
    public async Task BuildAppendsVerifiedReplacementAndUpdatesEntry()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] original = [1, 2, 3, 4, 5];
        byte[] replacementBytes = [9, 8, 7];
        string replacementPath = Path.Combine(directory, "replacement.bin");
        await File.WriteAllBytesAsync(replacementPath, replacementBytes, token);
        using var store = new ReplacementStagingStore(directory);
        StagedReplacement replacement = await store.StageAsync(replacementPath, token);
        using var fatInput = new MemoryStream(
            CreateIndex(new(1, original.Length, 0, original.Length, FatV10CompressionScheme.None, false)),
            false);
        using var datInput = new MemoryStream(original, false);
        using var fatOutput = new MemoryStream();
        using var datOutput = new MemoryStream();

        FatV10ArchivePatchBuildResult result = await FatV10ArchivePatchBuilder.BuildAsync(
            fatInput,
            datInput,
            fatOutput,
            datOutput,
            new Dictionary<int, StagedReplacement> { [0] = replacement },
            store,
            token);

        FatV10Index rebuilt = FatV10IndexReader.Read(
            new MemoryStream(fatOutput.ToArray(), false),
            datOutput.Length);
        FatV10Entry entry = Assert.Single(rebuilt.Entries);
        Assert.Equal(16, entry.Offset);
        Assert.Equal(3, entry.StoredSize);
        Assert.Equal(3, entry.UncompressedSize);
        Assert.Equal(FatV10CompressionScheme.None, entry.CompressionScheme);
        Assert.Equal(original, datOutput.ToArray()[..original.Length]);
        Assert.Equal(replacementBytes, datOutput.ToArray()[16..]);
        Assert.Equal(1, result.ReplacementCount);
    }

    [Fact]
    public async Task BuildRejectsInvalidIndexBeforeWritingOutputs()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        using var store = new ReplacementStagingStore(directory);
        string replacementPath = Path.Combine(directory, "replacement.bin");
        await File.WriteAllBytesAsync(replacementPath, [1], token);
        StagedReplacement replacement = await store.StageAsync(replacementPath, token);
        using var fatInput = new MemoryStream(CreateIndex(new(1, 1, 0, 1, FatV10CompressionScheme.None, false)), false);
        using var datInput = new MemoryStream(new byte[] { 1 }, false);
        using var fatOutput = new MemoryStream();
        using var datOutput = new MemoryStream();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            FatV10ArchivePatchBuilder.BuildAsync(
                fatInput,
                datInput,
                fatOutput,
                datOutput,
                new Dictionary<int, StagedReplacement> { [1] = replacement },
                store,
                token));

        Assert.Equal(0, fatOutput.Length);
        Assert.Equal(0, datOutput.Length);
    }

    public void Dispose() => Directory.Delete(directory, true);

    private static byte[] CreateIndex(FatV10Entry entry)
    {
        byte[] bytes = new byte[
            FatV10IndexSummaryReader.HeaderSize +
            FatV10IndexSummaryReader.EntrySize +
            FatV10IndexSummaryReader.TrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(20), 1);
        FatV10EntryWriter.Write(entry, bytes.AsSpan(FatV10IndexSummaryReader.HeaderSize, FatV10IndexSummaryReader.EntrySize));
        return bytes;
    }
}
