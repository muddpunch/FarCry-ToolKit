using System.Buffers.Binary;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10ArchivePatchFileBuilderTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit.Tests",
        Guid.NewGuid().ToString("N"));

    public FatV10ArchivePatchFileBuilderTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task BuildPublishesValidatedPairWithoutChangingSource()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair source = CreateSourcePair([1, 2, 3]);
        byte[] sourceFat = await File.ReadAllBytesAsync(source.FatPath, token);
        string replacementPath = Path.Combine(directory, "replacement.bin");
        await File.WriteAllBytesAsync(replacementPath, [9, 8], token);
        using var store = new ReplacementStagingStore(directory);
        StagedReplacement replacement = await store.StageAsync(replacementPath, token);
        var destination = new ArchivePair(
            Path.Combine(directory, "output.fat"),
            Path.Combine(directory, "output.dat"));

        FatV10ArchivePatchFileBuildResult result = await FatV10ArchivePatchFileBuilder.BuildAsync(
            source,
            destination,
            new Dictionary<int, StagedReplacement> { [0] = replacement },
            store,
            token);

        Assert.Equal(destination, result.OutputPair);
        Assert.Equal(sourceFat, await File.ReadAllBytesAsync(source.FatPath, token));
        Assert.Equal(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(source.DatPath, token));
        using FileStream fat = File.OpenRead(destination.FatPath);
        FatV10Entry entry = Assert.Single(
            FatV10IndexReader.Read(fat, new FileInfo(destination.DatPath).Length).Entries);
        Assert.Equal(16, entry.Offset);
        Assert.Equal(new byte[] { 9, 8 }, (await File.ReadAllBytesAsync(destination.DatPath, token))[16..]);
    }

    [Fact]
    public async Task BuildRejectsExistingDestinationWithoutChangingIt()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair source = CreateSourcePair([1]);
        var destination = new ArchivePair(
            Path.Combine(directory, "output.fat"),
            Path.Combine(directory, "output.dat"));
        await File.WriteAllBytesAsync(destination.FatPath, [7], token);
        using var store = new ReplacementStagingStore(directory);

        await Assert.ThrowsAsync<IOException>(() => FatV10ArchivePatchFileBuilder.BuildAsync(
            source,
            destination,
            new Dictionary<int, StagedReplacement>(),
            store,
            token));

        Assert.Equal(new byte[] { 7 }, await File.ReadAllBytesAsync(destination.FatPath, token));
        Assert.False(File.Exists(destination.DatPath));
    }

    [Fact]
    public async Task SourceValidationFailureDoesNotPublishDestination()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair source = CreateSourcePair([1, 2, 3]);
        byte[] sourceFat = await File.ReadAllBytesAsync(source.FatPath, token);
        byte[] sourceDat = await File.ReadAllBytesAsync(source.DatPath, token);
        var destination = new ArchivePair(
            Path.Combine(directory, "output.fat"),
            Path.Combine(directory, "output.dat"));
        using var store = new ReplacementStagingStore(directory);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            FatV10ArchivePatchFileBuilder.BuildAsync(
                source,
                destination,
                new Dictionary<int, StagedReplacement>(),
                store,
                static (_, _) => throw new InvalidDataException("Source validation failed."),
                token));

        Assert.Equal(sourceFat, await File.ReadAllBytesAsync(source.FatPath, token));
        Assert.Equal(sourceDat, await File.ReadAllBytesAsync(source.DatPath, token));
        Assert.False(File.Exists(destination.FatPath));
        Assert.False(File.Exists(destination.DatPath));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ArchivePair CreateSourcePair(byte[] data)
    {
        string fatPath = Path.Combine(directory, "source.fat");
        string datPath = Path.Combine(directory, "source.dat");
        byte[] fat = new byte[
            FatV10IndexSummaryReader.HeaderSize +
            FatV10IndexSummaryReader.EntrySize +
            FatV10IndexSummaryReader.TrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(fat, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(4), FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(20), 1);
        FatV10EntryWriter.Write(
            new(1, data.Length, 0, data.Length, FatV10CompressionScheme.None, false),
            fat.AsSpan(FatV10IndexSummaryReader.HeaderSize, FatV10IndexSummaryReader.EntrySize));
        File.WriteAllBytes(fatPath, fat);
        File.WriteAllBytes(datPath, data);
        return new(fatPath, datPath);
    }
}
