using System.Text;
using Dunia.Formats.Hashing;

namespace Dunia.Formats.Tests.Hashing;

public sealed class DuniaPathNameDiscoveryTests
{
    [Fact]
    public async Task DiscoverAsyncMatchesAsciiAndNormalizesPath()
    {
        const string path = "Graphics/Weapons/Rifle.xbg";
        using var input = new MemoryStream(Encoding.ASCII.GetBytes($"ignored\0{path}\0"), false);

        DuniaPathNameDiscoveryResult result = await DuniaPathNameDiscovery.DiscoverAsync(
            input,
            [DuniaPathHash.Compute(path)],
            cancellationToken: TestContext.Current.CancellationToken);

        DuniaPathNameDiscoveryMatch match = Assert.Single(result.Matches);
        Assert.Equal("graphics\\weapons\\rifle.xbg", match.Name);
        Assert.Equal(DuniaPathHash.Compute(path), match.Hash);
    }

    [Fact]
    public async Task DiscoverAsyncMatchesUnalignedUtf16LittleEndianPath()
    {
        const string path = "worlds\\example\\terrain.material.bin";
        byte[] encoded = Encoding.Unicode.GetBytes(path);
        byte[] data = new byte[encoded.Length + 5];
        encoded.CopyTo(data, 1);
        using var input = new MemoryStream(data, false);

        DuniaPathNameDiscoveryResult result = await DuniaPathNameDiscovery.DiscoverAsync(
            input,
            [DuniaPathHash.Compute(path)],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(path, Assert.Single(result.Matches).Name);
    }

    [Fact]
    public async Task DiscoverAsyncMatchesPathTokenInsideAsciiText()
    {
        const string path = "ui\\menus\\map.feu";
        using var input = new MemoryStream(Encoding.ASCII.GetBytes($"asset={path} flags=1\0"), false);

        DuniaPathNameDiscoveryResult result = await DuniaPathNameDiscovery.DiscoverAsync(
            input,
            [DuniaPathHash.Compute(path)],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(path, Assert.Single(result.Matches).Name);
    }
}
