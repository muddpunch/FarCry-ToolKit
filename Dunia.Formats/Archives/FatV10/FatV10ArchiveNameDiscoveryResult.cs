namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10ArchiveNameDiscoveryResult(
    int ScannedEntryCount,
    int SkippedEntryCount,
    long CandidateCount,
    IReadOnlyList<FatV10ArchiveNameDiscoveryMatch> Matches);
