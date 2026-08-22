using Dunia.Formats.Archives;
using System.Security.Cryptography;

namespace Dunia.Formats.Tests.Archives;

public sealed class ArchiveBackupServiceTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit.Tests",
        Guid.NewGuid().ToString("N"));

    public ArchiveBackupServiceTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task EnsureCreatedAsyncCreatesByteExactBackup()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string archivePath = CreateArchive([0x00, 0x7F, 0x80, 0xFF]);

        ArchiveBackupResult result = await ArchiveBackupService.EnsureCreatedAsync(archivePath, token);

        Assert.True(result.Created);
        Assert.Equal(archivePath + ".original", result.BackupPath);
        Assert.Equal(Convert.ToHexString(SHA256.HashData([0x00, 0x7F, 0x80, 0xFF])), result.Sha256);
        Assert.Equal(
            await File.ReadAllBytesAsync(archivePath, token),
            await File.ReadAllBytesAsync(result.BackupPath, token));
    }

    [Fact]
    public async Task EnsureCreatedAsyncNeverOverwritesExistingBackup()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string archivePath = CreateArchive([0x01]);
        ArchiveBackupResult first = await ArchiveBackupService.EnsureCreatedAsync(archivePath, token);
        await File.WriteAllBytesAsync(archivePath, [0x02], token);

        ArchiveBackupResult second = await ArchiveBackupService.EnsureCreatedAsync(archivePath, token);
        byte[] backup = await File.ReadAllBytesAsync(second.BackupPath, token);

        Assert.True(first.Created);
        Assert.False(second.Created);
        Assert.Equal(Convert.ToHexString(SHA256.HashData([0x01])), second.Sha256);
        Assert.Equal(new byte[] { 0x01 }, backup);
    }

    [Fact]
    public async Task EnsureCreatedAsyncPublishesExactlyOneConcurrentBackup()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string archivePath = CreateArchive(new byte[2 * 1024 * 1024]);

        ArchiveBackupResult[] results = await Task.WhenAll(
            ArchiveBackupService.EnsureCreatedAsync(archivePath, token),
            ArchiveBackupService.EnsureCreatedAsync(archivePath, token));

        Assert.Single(results, result => result.Created);
        Assert.Single(results, result => !result.Created);
        Assert.Empty(Directory.EnumerateFiles(directory, "*.tmp"));
    }

    public void Dispose() => Directory.Delete(directory, true);

    private string CreateArchive(byte[] content)
    {
        string path = Path.Combine(directory, "patch.fat");
        File.WriteAllBytes(path, content);
        return path;
    }
}
