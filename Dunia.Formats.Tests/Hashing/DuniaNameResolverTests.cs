using Dunia.Formats.Hashing;

namespace Dunia.Formats.Tests.Hashing;

public sealed class DuniaNameResolverTests
{
    [Fact]
    public void LoadIgnoresCommentsAndDeduplicatesNormalizedPaths()
    {
        using var input = new StringReader("""
            # source metadata

            Graphics/Example.XBT
            graphics\example.xbt
            ; another comment
            sounds/example.wav
            """);

        DuniaNameResolver resolver = DuniaNameResolver.Load(input);

        Assert.Equal(2, resolver.NameCount);
        Assert.Equal(2, resolver.HashCount);
        Assert.Equal(
            ["graphics\\example.xbt"],
            resolver.Resolve(DuniaPathHash.Compute("graphics\\example.xbt")));
        Assert.Empty(resolver.Resolve(ulong.MaxValue));
    }
}
