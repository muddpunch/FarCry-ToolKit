using System.Buffers.Binary;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10ArchivePatchApplyServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit.Tests",
        Guid.NewGuid().ToString("N"));

    public FatV10ArchivePatchApplyServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task ApplyBacksUpOriginalPairAndPublishesReplacement()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair pair = CreatePair([1, 2, 3]);
        byte[] originalFat = await File.ReadAllBytesAsync(pair.FatPath, token);
        byte[] originalDat = await File.ReadAllBytesAsync(pair.DatPath, token);
        string replacementPath = Path.Combine(directory, "replacement.bin");
        await File.WriteAllBytesAsync(replacementPath, [9, 8], token);
        using var store = new ReplacementStagingStore(directory);
        StagedReplacement replacement = await store.StageAsync(replacementPath, token);

        FatV10ArchivePatchApplyResult result = await FatV10ArchivePatchApplyService.ApplyAsync(
            pair,
            new Dictionary<int, StagedReplacement> { [0] = replacement },
            store,
            token);

        Assert.True(result.Backup.CreatedAny);
        Assert.Equal(originalFat, await File.ReadAllBytesAsync(pair.FatPath + ".original", token));
        Assert.Equal(originalDat, await File.ReadAllBytesAsync(pair.DatPath + ".original", token));
        using FileStream fat = File.OpenRead(pair.FatPath);
        FatV10Entry entry = Assert.Single(
            FatV10IndexReader.Read(fat, new FileInfo(pair.DatPath).Length).Entries);
        Assert.Equal(16, entry.Offset);
        Assert.Equal(new byte[] { 9, 8 }, (await File.ReadAllBytesAsync(pair.DatPath, token))[16..]);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task ApplyNeverOverwritesExistingOriginalBackups()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair pair = CreatePair([1]);
        await File.WriteAllBytesAsync(pair.FatPath + ".original", [7], token);
        await File.WriteAllBytesAsync(pair.DatPath + ".original", [8], token);
        using var store = new ReplacementStagingStore(directory);

        FatV10ArchivePatchApplyResult result = await FatV10ArchivePatchApplyService.ApplyAsync(
            pair,
            new Dictionary<int, StagedReplacement>(),
            store,
            token);

        Assert.False(result.Backup.CreatedAny);
        Assert.Equal(new byte[] { 7 }, await File.ReadAllBytesAsync(pair.FatPath + ".original", token));
        Assert.Equal(new byte[] { 8 }, await File.ReadAllBytesAsync(pair.DatPath + ".original", token));
    }

    [Fact]
    public async Task ApplyFailureBeforeBackupLeavesSourceUnchanged()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair pair = CreatePair([1]);
        byte[] originalFat = await File.ReadAllBytesAsync(pair.FatPath, token);
        using var store = new ReplacementStagingStore(directory);
        string replacementPath = Path.Combine(directory, "replacement.bin");
        await File.WriteAllBytesAsync(replacementPath, [2], token);
        StagedReplacement replacement = await store.StageAsync(replacementPath, token);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            FatV10ArchivePatchApplyService.ApplyAsync(
                pair,
                new Dictionary<int, StagedReplacement> { [1] = replacement },
                store,
                token));

        Assert.Equal(originalFat, await File.ReadAllBytesAsync(pair.FatPath, token));
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(pair.DatPath, token));
        Assert.False(File.Exists(pair.FatPath + ".original"));
        Assert.False(File.Exists(pair.DatPath + ".original"));
    }

    [Fact]
    public async Task PublicationValidationFailureRollsBackBothOriginalFiles()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair target = CreatePair([1, 2, 3], "target");
        ArchivePair built = CreatePair([9, 8], "built");
        byte[] originalFat = await File.ReadAllBytesAsync(target.FatPath, token);
        byte[] originalDat = await File.ReadAllBytesAsync(target.DatPath, token);
        string rollbackFat = Path.Combine(directory, "rollback.fat.tmp");
        string rollbackDat = Path.Combine(directory, "rollback.dat.tmp");
        var invalidExpectedEntry = new FatV10Entry(
            2,
            2,
            0,
            2,
            FatV10CompressionScheme.None,
            false);
        var expected = new FatV10ArchivePatchBuildResult(
            new(
                new(1, 1, FatV10IndexSummaryReader.EntrySize, 52),
                Array.AsReadOnly(new[] { invalidExpectedEntry })),
            2,
            1);

        Assert.Throws<InvalidDataException>(() => FatV10ArchivePatchApplyService.PublishWithRollback(
            target,
            built,
            rollbackFat,
            rollbackDat,
            expected));

        Assert.Equal(originalFat, await File.ReadAllBytesAsync(target.FatPath, token));
        Assert.Equal(originalDat, await File.ReadAllBytesAsync(target.DatPath, token));
        Assert.False(File.Exists(rollbackFat));
        Assert.False(File.Exists(rollbackDat));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ArchivePair CreatePair(byte[] data, string name = "source")
    {
        string fatPath = Path.Combine(directory, $"{name}.fat");
        string datPath = Path.Combine(directory, $"{name}.dat");
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
