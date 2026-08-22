namespace Dunia.Formats.Fcb;

public sealed record FcbNameCoverageReport(
    int OccurrenceCount,
    int ResolvedCount,
    int UnknownCount,
    int CollisionCount,
    IReadOnlyList<uint> UnknownHashes,
    IReadOnlyList<uint> CollisionHashes)
{
    public bool IsComplete => UnknownCount == 0 && CollisionCount == 0;
}
