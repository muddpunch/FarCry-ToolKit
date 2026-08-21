using System.Security.Cryptography;
using Dunia.Formats.Changes;

namespace Dunia.Formats.Tests.Changes;

public sealed class ReplacementStagingStoreTests : IDisposable
{
    private readonly string directory = Path.Combine(
        Path.GetTempPath(),
        "DuniaToolkit.Tests",
        Guid.NewGuid().ToString("N"));

    public ReplacementStagingStoreTests() => Directory.CreateDirectory(directory);

    [Fact]
    public async Task StageAsyncSnapshotsContentAndMetadata()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        byte[] original = [0x00, 0x7F, 0x80, 0xFF];
        string sourcePath = Path.Combine(directory, "replacement.dds");
        await File.WriteAllBytesAsync(sourcePath, original, token);
        using var store = new ReplacementStagingStore(directory);

        StagedReplacement replacement = await store.StageAsync(sourcePath, token);
        await File.WriteAllBytesAsync(sourcePath, [0x01], token);
        using var output = new MemoryStream();
        await store.CopyVerifiedToAsync(replacement, output, token);

        Assert.Equal("replacement.dds", replacement.OriginalFileName);
        Assert.Equal(original.Length, replacement.Length);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(original)), replacement.Sha256);
        Assert.Equal(original, output.ToArray());
    }

    [Fact]
    public async Task RemoveMakesReplacementUnavailable()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string sourcePath = Path.Combine(directory, "replacement.bin");
        await File.WriteAllBytesAsync(sourcePath, [0x01], token);
        using var store = new ReplacementStagingStore(directory);
        StagedReplacement replacement = await store.StageAsync(sourcePath, token);

        bool removed = store.Remove(replacement);

        Assert.True(removed);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.CopyVerifiedToAsync(replacement, new MemoryStream(), token));
    }

    [Fact]
    public async Task CopyVerifiedToAsyncRejectsTamperedStagingFile()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        string sourcePath = Path.Combine(directory, "replacement.bin");
        await File.WriteAllBytesAsync(sourcePath, [0x01], token);
        using var store = new ReplacementStagingStore(directory);
        StagedReplacement replacement = await store.StageAsync(sourcePath, token);
        string stagedPath = Assert.Single(Directory.EnumerateFiles(store.SessionPath, "*.bin"));
        await File.WriteAllBytesAsync(stagedPath, [0x02], token);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.CopyVerifiedToAsync(replacement, new MemoryStream(), token));
    }

    [Fact]
    public void DisposeDeletesOnlyOwnedSessionDirectory()
    {
        string unrelatedPath = Path.Combine(directory, "keep.txt");
        File.WriteAllText(unrelatedPath, "keep");
        var store = new ReplacementStagingStore(directory);
        string sessionPath = store.SessionPath;

        store.Dispose();

        Assert.False(Directory.Exists(sessionPath));
        Assert.True(File.Exists(unrelatedPath));
    }

    public void Dispose() => Directory.Delete(directory, true);
}

