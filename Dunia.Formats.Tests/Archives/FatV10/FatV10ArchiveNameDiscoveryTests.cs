using System.Text;
using Dunia.Formats.Archives.FatV10;
using Dunia.Formats.Hashing;

namespace Dunia.Formats.Tests.Archives.FatV10;

public sealed class FatV10ArchiveNameDiscoveryTests
{
    [Fact]
    public async Task DiscoverAsyncMatchesEmbeddedNormalizedResourcePaths()
    {
        const string path = "Graphics/Weapons/Rifle.xbt";
        byte[] payload = Encoding.ASCII.GetBytes($"ignored\0{path}\0other");
        using var data = new MemoryStream(payload, writable: false);
        var entry = new FatV10Entry(
            0,
            payload.Length,
            0,
            payload.Length,
            FatV10CompressionScheme.None,
            false);

        FatV10ArchiveNameDiscoveryResult result = await FatV10ArchiveNameDiscovery.DiscoverAsync(
            data,
            [entry],
            [DuniaPathHash.Compute(path)],
            cancellationToken: TestContext.Current.CancellationToken);

        FatV10ArchiveNameDiscoveryMatch match = Assert.Single(result.Matches);
        Assert.Equal("graphics\\weapons\\rifle.xbt", match.Name);
        Assert.Equal(0, match.SourceEntryIndex);
        Assert.Equal(1, result.ScannedEntryCount);
        Assert.Equal(0, result.SkippedEntryCount);
    }

    [Fact]
    public async Task DiscoverAsyncSkipsOversizedCompressedEntries()
    {
        using var data = new MemoryStream([0], writable: false);
        var entry = new FatV10Entry(0, 1024, 0, 1, FatV10CompressionScheme.Lz4, false);

        FatV10ArchiveNameDiscoveryResult result = await FatV10ArchiveNameDiscovery.DiscoverAsync(
            data,
            [entry],
            [],
            maxDecodedEntrySize: 128,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Empty(result.Matches);
        Assert.Equal(0, result.ScannedEntryCount);
        Assert.Equal(1, result.SkippedEntryCount);
    }
}
