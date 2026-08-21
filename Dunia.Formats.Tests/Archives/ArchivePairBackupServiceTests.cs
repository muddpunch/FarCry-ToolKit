using Dunia.Formats.Archives;

namespace Dunia.Formats.Tests.Archives;

public sealed class ArchivePairBackupServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit.Tests",
        Guid.NewGuid().ToString("N"));

    public ArchivePairBackupServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task EnsureCreatedAsyncBacksUpCompletePair()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair pair = CreatePair([0x01], [0x02]);

        ArchivePairBackupResult result = await ArchivePairBackupService.EnsureCreatedAsync(pair, token);
        byte[] fatBackup = await File.ReadAllBytesAsync(result.Fat.BackupPath, token);
        byte[] datBackup = await File.ReadAllBytesAsync(result.Dat.BackupPath, token);

        Assert.True(result.CreatedAny);
        Assert.True(result.Fat.Created);
        Assert.True(result.Dat.Created);
        Assert.Equal(new byte[] { 0x01 }, fatBackup);
        Assert.Equal(new byte[] { 0x02 }, datBackup);
    }

    [Fact]
    public async Task EnsureCreatedAsyncPreflightsBothSourcesBeforeCreatingBackup()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair pair = CreatePair([0x01], [0x02]);
        File.Delete(pair.DatPath);

        await Assert.ThrowsAsync<FileNotFoundException>(() =>
            ArchivePairBackupService.EnsureCreatedAsync(pair, token));

        Assert.False(File.Exists(pair.FatPath + ".original"));
        Assert.False(File.Exists(pair.DatPath + ".original"));
    }

    [Fact]
    public async Task EnsureCreatedAsyncReportsExistingBackupsWithoutReplacingThem()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        ArchivePair pair = CreatePair([0x01], [0x02]);
        await ArchivePairBackupService.EnsureCreatedAsync(pair, token);
        await File.WriteAllBytesAsync(pair.FatPath, [0x03], token);
        await File.WriteAllBytesAsync(pair.DatPath, [0x04], token);

        ArchivePairBackupResult result = await ArchivePairBackupService.EnsureCreatedAsync(pair, token);
        byte[] fatBackup = await File.ReadAllBytesAsync(result.Fat.BackupPath, token);
        byte[] datBackup = await File.ReadAllBytesAsync(result.Dat.BackupPath, token);

        Assert.False(result.CreatedAny);
        Assert.Equal(new byte[] { 0x01 }, fatBackup);
        Assert.Equal(new byte[] { 0x02 }, datBackup);
    }

    [Fact]
    public async Task EnsureCreatedAsyncRejectsSameSourceForBothFiles()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string path = Path.Combine(directory, "archive.bin");
        await File.WriteAllBytesAsync(path, [0x01], token);
        var pair = new ArchivePair(path, path);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            ArchivePairBackupService.EnsureCreatedAsync(pair, token));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private ArchivePair CreatePair(byte[] fat, byte[] dat)
    {
        string fatPath = Path.Combine(directory, "patch.fat");
        string datPath = Path.Combine(directory, "patch.dat");
        File.WriteAllBytes(fatPath, fat);
        File.WriteAllBytes(datPath, dat);
        return new(fatPath, datPath);
    }
}
