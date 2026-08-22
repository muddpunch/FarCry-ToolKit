using Dunia.Formats.Hashing;

namespace Dunia.Formats.Tests.Hashing;

public sealed class DuniaNameCoverageAnalyzerTests
{
    [Fact]
    public void AnalyzeCountsEntriesAndReturnsDistinctUnknownHashes()
    {
        var resolver = new DuniaNameResolver();
        resolver.Add("known.bin");
        ulong known = DuniaPathHash.Compute("known.bin");
        const ulong unknown = 0x1234;

        DuniaNameCoverageReport report = DuniaNameCoverageAnalyzer.Analyze(
            [known, unknown, unknown],
            resolver);

        Assert.Equal(3, report.EntryCount);
        Assert.Equal(1, report.ResolvedEntryCount);
        Assert.Equal(2, report.UnknownEntryCount);
        Assert.Equal(0, report.CollisionEntryCount);
        Assert.Equal([unknown], report.UnknownHashes);
        Assert.False(report.IsComplete);
    }

    [Fact]
    public void AnalyzeReportsCompleteCatalog()
    {
        var resolver = new DuniaNameResolver();
        resolver.Add("known.bin");

        DuniaNameCoverageReport report = DuniaNameCoverageAnalyzer.Analyze(
            [DuniaPathHash.Compute("known.bin")],
            resolver);

        Assert.True(report.IsComplete);
    }
}
