using System.Buffers.Binary;
using System.Security.Cryptography;
using Dunia.Formats.Archives;
using Dunia.Formats.Archives.FatV10;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10ArchiveRestoreServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit.Tests",
        Guid.NewGuid().ToString("N"));

    public FatV10ArchiveRestoreServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task RestorePublishesVerifiedBackupPairAndPreservesBackups()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair target = CreatePair("common", [9, 8]);
        ArchivePair backup = CreatePair("common", [1, 2, 3], ".original");
        byte[] backupFat = await File.ReadAllBytesAsync(backup.FatPath, token);
        byte[] backupDat = await File.ReadAllBytesAsync(backup.DatPath, token);
        string fatSha256 = Convert.ToHexString(SHA256.HashData(backupFat));
        string datSha256 = Convert.ToHexString(SHA256.HashData(backupDat));

        FatV10ArchiveRestoreResult result = await FatV10ArchiveRestoreService.RestoreAsync(
            target,
            fatSha256,
            datSha256,
            token);

        Assert.True(result.Verified);
        Assert.Equal(backupFat, await File.ReadAllBytesAsync(target.FatPath, token));
        Assert.Equal(backupDat, await File.ReadAllBytesAsync(target.DatPath, token));
        Assert.Equal(backupFat, await File.ReadAllBytesAsync(backup.FatPath, token));
        Assert.Equal(backupDat, await File.ReadAllBytesAsync(backup.DatPath, token));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task RestoreRejectsUnexpectedBackupHashWithoutChangingTarget()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair target = CreatePair("common", [9, 8]);
        ArchivePair backup = CreatePair("common", [1, 2, 3], ".original");
        byte[] targetFat = await File.ReadAllBytesAsync(target.FatPath, token);
        byte[] targetDat = await File.ReadAllBytesAsync(target.DatPath, token);
        string datSha256 = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(backup.DatPath, token)));

        await Assert.ThrowsAsync<InvalidDataException>(() => FatV10ArchiveRestoreService.RestoreAsync(
            target,
            new string('0', 64),
            datSha256,
            token));

        Assert.Equal(targetFat, await File.ReadAllBytesAsync(target.FatPath, token));
        Assert.Equal(targetDat, await File.ReadAllBytesAsync(target.DatPath, token));
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    [Fact]
    public async Task PostPublicationHashFailureRollsBackBothFiles()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair target = CreatePair("target", [9, 8]);
        ArchivePair restored = CreatePair("restored", [1, 2, 3]);
        byte[] targetFat = await File.ReadAllBytesAsync(target.FatPath, token);
        byte[] targetDat = await File.ReadAllBytesAsync(target.DatPath, token);
        string expectedFatSha256 = Convert.ToHexString(SHA256.HashData(
            await File.ReadAllBytesAsync(restored.FatPath, token)));
        string rollbackFat = Path.Combine(directory, "rollback.fat.tmp");
        string rollbackDat = Path.Combine(directory, "rollback.dat.tmp");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            FatV10ArchiveRestoreService.PublishWithRollbackAsync(
                target,
                restored,
                rollbackFat,
                rollbackDat,
                expectedFatSha256,
                new string('0', 64),
                token));

        Assert.Equal(targetFat, await File.ReadAllBytesAsync(target.FatPath, token));
        Assert.Equal(targetDat, await File.ReadAllBytesAsync(target.DatPath, token));
        Assert.False(File.Exists(rollbackFat));
        Assert.False(File.Exists(rollbackDat));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ArchivePair CreatePair(string name, byte[] payload, string suffix = "")
    {
        var pair = new ArchivePair(
            Path.Combine(directory, $"{name}.fat{suffix}"),
            Path.Combine(directory, $"{name}.dat{suffix}"));
        byte[] fat = new byte[
            FatV10IndexSummaryReader.HeaderSize +
            FatV10IndexSummaryReader.EntrySize +
            FatV10IndexSummaryReader.TrailerSize];
        BinaryPrimitives.WriteUInt32LittleEndian(fat, FatV10IndexSummaryReader.Signature);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(4), FatV10IndexSummaryReader.Version);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(8), 1);
        BinaryPrimitives.WriteInt32LittleEndian(fat.AsSpan(20), 1);
        FatV10EntryWriter.Write(
            new(1, payload.Length, 0, payload.Length, FatV10CompressionScheme.None, false),
            fat.AsSpan(FatV10IndexSummaryReader.HeaderSize, FatV10IndexSummaryReader.EntrySize));
        File.WriteAllBytes(pair.FatPath, fat);
        File.WriteAllBytes(pair.DatPath, payload);
        return pair;
    }
}
