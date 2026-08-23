namespace Dunia.Formats.Archives.FatV10;

public sealed record FatV10ArchiveNameDiscoveryProgress(
    int ProcessedEntryCount,
    int TotalEntryCount,
    int MatchCount);
