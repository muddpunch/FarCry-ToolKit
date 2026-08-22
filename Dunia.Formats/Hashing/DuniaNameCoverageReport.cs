namespace Dunia.Formats.Hashing;

public sealed record DuniaNameCoverageReport(
    int EntryCount,
    int ResolvedEntryCount,
    int UnknownEntryCount,
    int CollisionEntryCount,
    IReadOnlyList<ulong> UnknownHashes,
    IReadOnlyList<ulong> CollisionHashes)
{
    public bool IsComplete => UnknownEntryCount == 0 && CollisionEntryCount == 0;
}
