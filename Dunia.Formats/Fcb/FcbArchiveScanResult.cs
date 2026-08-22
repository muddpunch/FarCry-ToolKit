namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveScanResult(
    IReadOnlyList<FcbArchiveMatch> Matches,
    int ScannedEntryCount,
    int SkippedEntryCount);
