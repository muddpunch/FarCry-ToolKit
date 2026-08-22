using System.Text;
using Dunia.Formats.Fcb;
using Dunia.Formats.Hashing;

namespace Dunia.Formats.Tests.Fcb;

public sealed class FcbNameDiscoveryTests
{
    [Fact]
    public void ScanAsciiReturnsOnlyExactTargetMatchesAndUnknownHashes()
    {
        byte[] data = Encoding.ASCII.GetBytes("ignored\0KnownType\0KnownType\0Other\0");
        using var input = new MemoryStream(data, false);
        uint known = DuniaCrc32.Compute("KnownType");

        FcbNameDiscoveryResult result = FcbNameDiscovery.ScanAscii(input, [known, 0x12345678]);

        FcbNameDiscoveryMatch match = Assert.Single(result.Matches);
        Assert.Equal(known, match.Hash);
        Assert.Equal("KnownType", match.Name);
        Assert.Equal(8, match.SourceOffset);
        Assert.Equal(new uint[] { 0x12345678 }, result.UnknownHashes);
        Assert.Equal(4, result.CandidateCount);
    }

    [Fact]
    public void ScanAsciiHandlesCandidatesAcrossReadBufferBoundary()
    {
        byte[] data = new byte[(64 * 1024) + 8];
        Array.Fill(data, (byte)0);
        Encoding.ASCII.GetBytes("Boundary").CopyTo(data, (64 * 1024) - 4);
        using var input = new MemoryStream(data, false);

        FcbNameDiscoveryResult result = FcbNameDiscovery.ScanAscii(
            input,
            [DuniaCrc32.Compute("Boundary")]);

        Assert.Equal("Boundary", Assert.Single(result.Matches).Name);
    }
}
