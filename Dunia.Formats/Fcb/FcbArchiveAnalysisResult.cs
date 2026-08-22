namespace Dunia.Formats.Fcb;

public sealed record FcbArchiveAnalysisResult(
    IReadOnlyList<FcbArchiveResource> Resources,
    IReadOnlyDictionary<uint, int> TypeHashOccurrences,
    IReadOnlyDictionary<uint, int> FieldHashOccurrences,
    int ScannedEntryCount,
    int SkippedEntryCount);
