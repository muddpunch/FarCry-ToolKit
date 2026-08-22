namespace Dunia.Formats.Fcb;

public sealed record FcbNameDiscoveryResult(
    int TargetHashCount,
    long CandidateCount,
    IReadOnlyList<FcbNameDiscoveryMatch> Matches,
    IReadOnlyList<uint> UnknownHashes);
