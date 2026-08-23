using Dunia.Formats.Hashing;

namespace Dunia.Formats.Tests.Hashing;

public sealed class DuniaNameCatalogStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        nameof(DuniaNameCatalogStoreTests),
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task MergeAsyncPersistsNormalizedUniqueNamesAcrossCalls()
    {
        string path = Path.Combine(_directory, "archive.filelist");

        await DuniaNameCatalogStore.MergeAsync(
            path,
            ["Graphics/Weapons/Rifle.xbt", "graphics\\weapons\\rifle.xbt"],
            TestContext.Current.CancellationToken);
        int count = await DuniaNameCatalogStore.MergeAsync(
            path,
            ["Worlds/Main.fcb"],
            TestContext.Current.CancellationToken);

        Assert.Equal(2, count);
        Assert.Equal(
            ["graphics\\weapons\\rifle.xbt", "worlds\\main.fcb"],
            await File.ReadAllLinesAsync(path, TestContext.Current.CancellationToken));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, true);
        }
    }
}
